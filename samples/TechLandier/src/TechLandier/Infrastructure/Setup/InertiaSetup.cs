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
            o.Share<SharedAuthDto>("auth", AuthHttp.SharedAuth);
            // Category tiles for the storefront menu and footer: resolved once, kept by the client, and served from the
            // catalog cache. Home and the products index send their own copy of the same prop.
            // No expiry on purpose: a TTL puts a moving expiresAt timestamp in every page object, which makes each
            // response unique and defeats the SSR render cache (measured: product page p50 16ms -> 51ms).
            o.Share("categories", _ =>
            {
                static async Task<List<CategoryTile>> Load(StoreService service, CancellationToken token) =>
                    await service.CategoryTilesAsync(token);
                return Prop<StoreService, List<CategoryTile>>(Load)
                    .Cache("catalog:categories", StoreCache.CatalogFreshFor, StoreCache.CatalogStaleFor, [StoreCache.Catalog])
                    .Once("categories");
            });
            o.Share("notifications", ctx =>
            {
                if (ctx.User.UserIdOrNull() is not { } id) return Prop(() => (NotificationSummary?)null);
                // Inertia.Net 1.9 supplies the service from the request, or from a dedicated scope for a parallel
                // loader or a stale-while-revalidate refresh that outlives the response.
                async Task<NotificationSummary> Load(AlertsService service, CancellationToken token) =>
                    await service.SummaryAsync(id, token);
                return Prop<AlertsService, NotificationSummary>(Load)
                    .Cache(NotificationsCache.Key(id), NotificationsCache.FreshFor, NotificationsCache.StaleFor,
                        [NotificationsCache.Tag(id)])
                    // The bell and the alerts page follow creation/read events on the owner's own channel; publish
                    // points remove the NotificationsCache entry first, so each reload reads fresh rows.
                    .Live(UserEvents.Notification(id))
                    .Throttle(TimeSpan.FromMilliseconds(200));
            });
            // The page-data script and Vite tags carry the nonce SecurityHeadersMiddleware put in the policy.
            o.CspNonce = SecurityHeadersMiddleware.Nonce;
            o.Pages.Directory = "ClientApp/pages";
            // Generate typed page props, shared props and flash data from the C# contracts. routes.ts is built from
            // the named endpoints in Features/*; both files are rewritten only when their content changes.
            o.TypeScript.OutputPath = "ClientApp/types/inertia.d.ts";
            o.TypeScript.RoutesOutputPath = "ClientApp/types/routes.ts";
            o.TypeScript.AddGeneratedPages().SharedProps<SharedPageProps>().FlashData<Flash>();
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
