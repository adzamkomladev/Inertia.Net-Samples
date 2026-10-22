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
            o.Share("notifications", ctx => Prop(async ct =>
            {
                if (ctx.User.UserIdOrNull() is not { } id) return null;
                await using var scope = ctx.RequestServices.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<AlertsService>().SummaryAsync(id, ct);
            }));
            o.Pages.Directory = "ClientApp/pages";
            o.Vite.Prefetch = VitePrefetch.Waterfall;
            o.Ssr.Mode = config.GetValue("Inertia:Ssr", true) ? SsrMode.Preferred : SsrMode.Disabled;
            // Identical page JSON reuses the server-rendered HTML for a few minutes (the benchmark test zeroes this).
            var ssrCacheSeconds = config.GetValue("Inertia:SsrCacheSeconds", 300);
            if (ssrCacheSeconds < 0)
                throw new InvalidOperationException("Inertia:SsrCacheSeconds must be zero or a positive number of seconds.");
            o.Ssr.Cache.Duration = TimeSpan.FromSeconds(ssrCacheSeconds);
            // Vite compiles a page on its first dev request, so Development gets a longer timeout.
            var timeout = config.GetValue<double>("Inertia:SsrTimeoutSeconds", builder.Environment.IsDevelopment() ? 30 : 5);
            if (!double.IsFinite(timeout) || timeout <= 0) throw new InvalidOperationException("SSR_TIMEOUT_SECONDS must be a positive, finite number.");
            o.Ssr.Timeout = TimeSpan.FromSeconds(timeout);
            if (config.GetValue("Inertia:Ssr", true)) o.Ssr.UseNodeProcess(n => n.Executable = config["Inertia:SsrExecutable"] ?? "bun");
        });

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
