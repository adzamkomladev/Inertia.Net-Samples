using Inertia.Net;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using CarParker.Data;
using CarParker.Features;
using CarParker.Features.Alerts;
using CarParker.Infrastructure.Caching;

namespace CarParker.Infrastructure.Setup;

public static class InertiaSetup
{
    public static WebApplicationBuilder AddBlogInertia(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var secureCookies = config.GetValue("Auth:SecureCookies", !builder.Environment.IsDevelopment());
        builder.Services.AddValidation();
        builder.Services.AddProblemDetails();
        // Page JSON and HTML compress five to ten times (see the Inertia.Net README). The app sits behind a
        // TLS-terminating proxy, so compression must be enabled for HTTPS.
        builder.Services.AddResponseCompression(o => o.EnableForHttps = true);
        // The Inertia DevTools extension protocol; records in Development only by default.
        builder.Services.AddInertiaDevTools();
        builder.Services.AddInertia(o =>
        {
            o.Share("appName", Once(() => config["App:Name"] ?? "CarParker"));
            o.Share("auth", AuthHttp.SharedAuth);
            o.Share("notifications", ctx =>
            {
                if (ctx.User.UserIdOrNull() is not { } id) return Prop(() => (NotificationSummary?)null);
                return Prop(async ct => await ctx.RequestServices.GetRequiredService<AlertsService>().SummaryAsync(id, ct))
                    .Cache(AlertsCache.Key(id), AlertsCache.FreshFor, AlertsCache.StaleFor, [AlertsCache.Tag(id)]);
            });
            // Cross-sample cookie isolation on localhost: other samples use their own prefixes.
            o.State.CookieName = ".cpr.state";
            o.State.CookieSecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            // Development check: rendering a component without a matching page file throws and names the closest match.
            o.Pages.Directory = "ClientApp/pages";
            // After the page loads, prefetch at low priority the code-split chunks the next navigation will import.
            o.Vite.Prefetch = VitePrefetch.Waterfall;
            o.Ssr.Mode = config.GetValue("Inertia:Ssr", true) ? SsrMode.Preferred : SsrMode.Disabled;
            // Reuse a server-rendered page for a few minutes when the page JSON is identical (Inertia.Net 1.4). Anonymous
            // visitors share one entry per URL; later Inertia visits still fetch fresh JSON. The opt-in performance test
            // zeroes this so the benchmark keeps measuring the SSR parser, see InertiaPerformanceTests.
            var ssrCacheSeconds = config.GetValue("Inertia:SsrCacheSeconds", 300);
            if (ssrCacheSeconds < 0)
                throw new InvalidOperationException("Inertia:SsrCacheSeconds must be zero or a positive number of seconds.");
            o.Ssr.Cache.Duration = TimeSpan.FromSeconds(ssrCacheSeconds);
            // Past the fresh window the last render is still served while one request re-renders (1.5), so a busy
            // storefront never makes a visitor wait behind a burst of identical page loads.
            var ssrStaleSeconds = config.GetValue("Inertia:SsrStaleSeconds", 60);
            if (ssrStaleSeconds < 0)
                throw new InvalidOperationException("Inertia:SsrStaleSeconds must be zero or a positive number of seconds.");
            o.Ssr.Cache.StaleFor = ssrCacheSeconds > 0 ? TimeSpan.FromSeconds(ssrStaleSeconds) : TimeSpan.Zero;
            // Vite may compile a page on its first dev request, so Development gets a longer timeout.
            var timeout = config.GetValue<double>("Inertia:SsrTimeoutSeconds", builder.Environment.IsDevelopment() ? 30 : 5);
            if (!double.IsFinite(timeout) || timeout <= 0) throw new InvalidOperationException("SSR_TIMEOUT_SECONDS must be a positive, finite number.");
            o.Ssr.Timeout = TimeSpan.FromSeconds(timeout);
            // The SSR bundle is plain JS; bun runs it like node (bundle path stays ssr/ssr.js). Each replica
            // supervises its own loopback Bun process; the shared cache is what all replicas reuse.
            if (config.GetValue("Inertia:Ssr", true)) o.Ssr.UseNodeProcess(n => n.Executable = config["Inertia:SsrExecutable"] ?? "bun");
        });

        // A shared Redis cache makes prop invalidation and SSR renders visible to every replica. Without
        // Redis (single app, tests) Inertia.Net's per-process memory store remains in place.
        if (ReplicaSetup.HasRedis(config))
        {
            var cacheOptions = RedisInertiaCacheOptions.FromConfiguration(config, ReplicaSetup.Namespace(builder));
            builder.Services.Replace(ServiceDescriptor.Singleton<IInertiaCacheStore>(sp =>
                new RedisInertiaCacheStore(
                    sp.GetRequiredService<IConnectionMultiplexer>(),
                    cacheOptions,
                    sp.GetRequiredService<ILogger<RedisInertiaCacheStore>>())));
            builder.Services.AddHealthChecks()
                .AddCheck<RedisInertiaCacheHealthCheck>("inertia-cache", failureStatus: HealthStatus.Degraded, tags: ["ready"]);
        }

        // SSR only the public pages; everything else renders client-side from app.html and never contacts the SSR process.
        var rootView = builder.Services.Single(d => d.ServiceType == typeof(IInertiaRootView)).ImplementationType!;
        builder.Services.AddSingleton<IInertiaRootView>(sp => new PublicPostRootView(
            (IInertiaRootView)ActivatorUtilities.CreateInstance(sp, rootView),
            sp.GetRequiredService<ViteAssets>(), sp.GetRequiredService<IHostEnvironment>()));

        var health = builder.Services.AddHealthChecks().AddCheck<MongoHealthCheck>("mongodb");
        // SSR and cache outages are Degraded: Inertia falls back to client rendering and the database.
        if (config.GetValue("Inertia:Ssr", true)) health.AddInertiaSsr("inertia-ssr", HealthStatus.Degraded);
        return builder;
    }
}
