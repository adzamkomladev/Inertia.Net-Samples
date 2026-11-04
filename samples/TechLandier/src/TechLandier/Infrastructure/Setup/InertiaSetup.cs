using Inertia.Net;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Features;

namespace TechLandier.Infrastructure.Setup;

public static class InertiaSetup
{
    public static WebApplicationBuilder AddStoreInertia(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
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
                // Stale-while-revalidate refreshes (Inertia.Net 1.6.3) run on a background worker after the
                // response is gone, so the loader creates its own scope from the singleton factory instead
                // of the finished request's services.
                var scopes = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>();
                return Prop(async ct =>
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        return await scope.ServiceProvider.GetRequiredService<AlertsService>().SummaryAsync(id, ct);
                    })
                    .Cache(NotificationsCache.Key(id), NotificationsCache.FreshFor, NotificationsCache.StaleFor,
                        [NotificationsCache.Tag(id)])
                    // The bell and the alerts page follow creation/read events on the owner's own channel; publish
                    // points remove the NotificationsCache entry first, so each reload reads fresh rows.
                    .Live(UserEvents.Notification(id))
                    .Throttle(TimeSpan.FromMilliseconds(200));
            });
            o.Pages.Directory = "ClientApp/pages";
            o.Vite.Prefetch = VitePrefetch.Waterfall;
            o.Ssr.Mode = config.GetValue("Inertia:Ssr", true) ? SsrMode.Preferred : SsrMode.Disabled;
            // Identical page JSON reuses the server-rendered HTML for a few minutes (the benchmark test zeroes this).
            var ssrCacheSeconds = config.GetValue("Inertia:SsrCacheSeconds", 300);
            if (ssrCacheSeconds < 0)
                throw new InvalidOperationException("Inertia:SsrCacheSeconds must be zero or a positive number of seconds.");
            o.Ssr.Cache.Duration = TimeSpan.FromSeconds(ssrCacheSeconds);
            // Past the fresh window the last render is still served while one request re-renders (1.5), so the
            // storefront keeps answering during a burst instead of queueing every visitor behind the renderer.
            var ssrStaleSeconds = config.GetValue("Inertia:SsrStaleSeconds", 60);
            if (ssrStaleSeconds < 0)
                throw new InvalidOperationException("Inertia:SsrStaleSeconds must be zero or a positive number of seconds.");
            o.Ssr.Cache.StaleFor = ssrCacheSeconds > 0 ? TimeSpan.FromSeconds(ssrStaleSeconds) : TimeSpan.Zero;
            // Vite compiles a page on its first dev request, so Development gets a longer timeout.
            var timeout = config.GetValue<double>("Inertia:SsrTimeoutSeconds", builder.Environment.IsDevelopment() ? 30 : 5);
            if (!double.IsFinite(timeout) || timeout <= 0) throw new InvalidOperationException("SSR_TIMEOUT_SECONDS must be a positive, finite number.");
            o.Ssr.Timeout = TimeSpan.FromSeconds(timeout);
            if (config.GetValue("Inertia:Ssr", true)) o.Ssr.UseNodeProcess(n => n.Executable = config["Inertia:SsrExecutable"] ?? "bun");
        });

        // Live props (Inertia.Net 1.6): admin consoles listen on a private admins channel authorized by role
        // claims; customers and guests may join only their own notification channel and public order channels.
        builder.Services.AddInertiaLive().AddSignalR(o =>
            o.Authorize(async (context, channel, _) =>
            {
                if (channel.Name == "admins")
                    return await ValueTask.FromResult(context.User?.IsInRole("admin") == true);
                return await ValueTask.FromResult(context.UserIdentifier is { } id && channel.Name == $"users.{id}");
            }));

        // Only the public storefront pages are server-rendered.
        var rootView = builder.Services.Single(d => d.ServiceType == typeof(IInertiaRootView)).ImplementationType!;
        builder.Services.AddSingleton<IInertiaRootView>(sp => new StoreRootView(
            (IInertiaRootView)ActivatorUtilities.CreateInstance(sp, rootView),
            sp.GetRequiredService<ViteAssets>(), sp.GetRequiredService<IHostEnvironment>()));

        var health = builder.Services.AddHealthChecks().AddDbContextCheck<AppDb>();
        if (config.GetValue("Inertia:Ssr", true)) health.AddInertiaSsr();
        return builder;
    }
}
