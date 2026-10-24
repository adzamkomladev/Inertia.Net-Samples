using Inertia.Net;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TechLandier.Caching;
using TechLandier.Data;
using TechLandier.Features;
using TechLandier.Infrastructure;

namespace TechLandier.Infrastructure.Setup;

public static class InertiaSetup
{
    public static WebApplicationBuilder AddStoreInertia(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var secureCookies = config.GetValue("Auth:SecureCookies", !builder.Environment.IsDevelopment());
        // The shared prop/SSR store must be registered before Inertia so its TryAdd default cannot win.
        builder.Services.AddStoreCache(config, builder.Environment).AddInertiaPropInvalidator();

        builder.Services.AddValidation();
        builder.Services.AddProblemDetails();
        // The app sits behind a TLS-terminating proxy, so compression has to work over HTTPS too.
        builder.Services.AddResponseCompression(o => o.EnableForHttps = true);
        builder.Services.AddInertiaDevTools();
        builder.Services.AddInertia(o =>
        {
            o.Share("appName", Once(() => config["App:Name"] ?? "TechLandier"));
            o.Share("auth", AuthHttp.SharedAuth);
            o.Share("notifications", ctx =>
            {
                if (ctx.User.UserIdOrNull() is not { } id) return Prop(() => (NotificationSummary?)null);
                return Prop(async ct =>
                    {
                        await using var scope = ctx.RequestServices.CreateAsyncScope();
                        return await scope.ServiceProvider.GetRequiredService<AlertsService>().SummaryAsync(id, ct);
                    })
                    .Cache(NotificationsCache.Key(id), NotificationsCache.FreshFor, NotificationsCache.StaleFor,
                        [NotificationsCache.Tag(id)]);
            });
            o.Pages.Directory = "ClientApp/pages";
            o.Vite.Prefetch = VitePrefetch.Waterfall;
            o.State.CookieName = ".tld.state";
            o.State.CookieSecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

            var ssrEnabled = config.GetValue("Inertia:Ssr", true);
            o.Ssr.Mode = ssrEnabled ? SsrMode.Preferred : SsrMode.Disabled;
            if (ssrEnabled)
            {
                // A standalone SSR pool. There is deliberately no UseNodeProcess: MainApp never ships Bun.
                var urls = config.GetSection("Inertia:SsrUrls").Get<string[]>() ?? [];
                if (urls.Length == 0) urls = ["http://127.0.0.1:13714"];
                foreach (var value in urls)
                {
                    if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") || url.UserInfo.Length > 0)
                        throw new InvalidOperationException($"Inertia:SsrUrls contains an invalid SSR URL: {value}");
                    o.Ssr.Urls.Add(value.TrimEnd('/'));
                }
            }

            // Identical page JSON reuses the server-rendered HTML; the cache lives in the shared cache Redis.
            var ssrCacheSeconds = config.GetValue("Inertia:SsrCacheSeconds", 300);
            if (ssrCacheSeconds < 0)
                throw new InvalidOperationException("Inertia:SsrCacheSeconds must be zero or a positive number of seconds.");
            o.Ssr.Cache.Duration = TimeSpan.FromSeconds(ssrCacheSeconds);
            var ssrStaleSeconds = config.GetValue("Inertia:SsrStaleSeconds", 60);
            if (ssrStaleSeconds < 0)
                throw new InvalidOperationException("Inertia:SsrStaleSeconds must be zero or a positive number of seconds.");
            o.Ssr.Cache.StaleFor = ssrCacheSeconds > 0 ? TimeSpan.FromSeconds(ssrStaleSeconds) : TimeSpan.Zero;

            // A remote render is trusted input; keep the deadline short and trip the circuit quickly.
            var timeout = config.GetValue<double>("Inertia:SsrTimeoutSeconds", 2);
            if (!double.IsFinite(timeout) || timeout <= 0) throw new InvalidOperationException("SSR_TIMEOUT_SECONDS must be a positive, finite number.");
            o.Ssr.Timeout = TimeSpan.FromSeconds(timeout);
            o.Ssr.Concurrency.MaxActive = config.GetValue("Inertia:SsrMaxActive", 2);
            o.Ssr.Concurrency.MaxQueued = config.GetValue("Inertia:SsrMaxQueued", 8);
            o.Ssr.CircuitBreaker.FailureThreshold = config.GetValue("Inertia:SsrFailureThreshold", 2);
        });

        // Only the public storefront pages are server-rendered.
        var rootView = builder.Services.Single(d => d.ServiceType == typeof(IInertiaRootView)).ImplementationType!;
        builder.Services.AddSingleton<IInertiaRootView>(sp => new StoreRootView(
            (IInertiaRootView)ActivatorUtilities.CreateInstance(sp, rootView),
            sp.GetRequiredService<ViteAssets>(), sp.GetRequiredService<IHostEnvironment>()));

        var health = builder.Services.AddHealthChecks().AddDbContextCheck<AppDb>();
        if (config.GetValue("Inertia:Ssr", true)) health.AddInertiaSsr(failureStatus: HealthStatus.Degraded);
        return builder;
    }
}
