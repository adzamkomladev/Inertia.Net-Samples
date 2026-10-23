using Inkwell.Data;
using Inkwell.Features.Auth;
using Inkwell.Features.Notifications;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Inkwell.Infrastructure.Setup;

public static class InertiaSetup
{
    public static WebApplicationBuilder AddBlogInertia(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var services = builder.Services;

        services.AddValidation();
        services.AddProblemDetails();
        // Traffic arrives through a TLS-terminating proxy, so compression has to be enabled for HTTPS.
        services.AddResponseCompression(o => o.EnableForHttps = true);
        services.AddInertiaDevTools();
        services.AddInertia(o =>
        {
            o.Share("appName", Once(() => config["App:Name"] ?? "Inkwell"));
            o.Share("auth", ctx =>
            {
                if (ctx.User.UserId() is not { } id) return Prop(() => new SharedAuthDto(null));
                return Prop(async ct => await SharedAuth.LoadAsync(ctx, ct))
                    .Cache(AuthCache.Key(id), AuthCache.FreshFor, AuthCache.StaleFor, [AuthCache.Tag(id)]);
            });
            o.Share("notifications", ctx =>
            {
                if (ctx.User.UserId() is not { } id) return Prop(() => (NotificationSummary?)null);
                return Prop(async ct =>
                    {
                        await using var scope = ctx.RequestServices.CreateAsyncScope();
                        return await scope.ServiceProvider.GetRequiredService<NotificationsService>().SummaryAsync(id, ct);
                    })
                    .Cache(NotificationsCache.Key(id), NotificationsCache.FreshFor, NotificationsCache.StaleFor,
                        [NotificationsCache.Tag(id)]);
            });
            o.Pages.Directory = "ClientApp/pages";
            o.Vite.Prefetch = VitePrefetch.Waterfall;
            // Skipped while the Vite dev server runs (it does SSR itself) or the bundle is missing.
            o.Ssr.BundlePath = "ssr/ssr.js";
            o.Ssr.Mode = SsrMode.Preferred;
            // Anonymous visitors share one server-rendered entry per URL; the performance test zeroes this.
            var ssrCacheSeconds = config.GetValue("Inertia:SsrCacheSeconds", 300);
            if (ssrCacheSeconds < 0)
                throw new InvalidOperationException("Inertia:SsrCacheSeconds must be zero or a positive number of seconds.");
            o.Ssr.Cache.Duration = TimeSpan.FromSeconds(ssrCacheSeconds);
            // Past the fresh window the last render is still served while one request re-renders (1.5). A visitor never
            // waits for a render that a burst of identical page loads would otherwise queue behind.
            var ssrStaleSeconds = config.GetValue("Inertia:SsrStaleSeconds", 60);
            if (ssrStaleSeconds < 0)
                throw new InvalidOperationException("Inertia:SsrStaleSeconds must be zero or a positive number of seconds.");
            o.Ssr.Cache.StaleFor = ssrCacheSeconds > 0 ? TimeSpan.FromSeconds(ssrStaleSeconds) : TimeSpan.Zero;
            // Vite may compile a page on its first request, so development allows longer.
            var timeoutSeconds = config.GetValue<double>("SSR_TIMEOUT_SECONDS", builder.Environment.IsDevelopment() ? 30 : 5);
            if (!double.IsFinite(timeoutSeconds) || timeoutSeconds <= 0)
                throw new InvalidOperationException("SSR_TIMEOUT_SECONDS must be a positive finite number.");
            o.Ssr.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            o.Ssr.UseNodeProcess(n =>
            {
                n.BundlePath = "ssr/ssr.js";
                n.Executable = config["Inertia:SsrExecutable"] ?? "bun"; // the bundle is plain JS; Bun runs it like Node
            });
        });

        // Only the public feed and post pages go through SSR; private pages use the plain root view.
        var rootViewType = services.Single(d => d.ServiceType == typeof(IInertiaRootView)).ImplementationType!;
        services.AddSingleton<IInertiaRootView>(provider => new PublicPostRootView(
            (IInertiaRootView)ActivatorUtilities.CreateInstance(provider, rootViewType),
            provider.GetRequiredService<ViteAssets>(), provider.GetRequiredService<IHostEnvironment>()));

        services.AddHealthChecks()
            .AddDbContextCheck<AppDb>("db")
            .AddInertiaSsr(failureStatus: HealthStatus.Degraded);
        return builder;
    }
}
