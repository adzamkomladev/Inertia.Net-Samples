using Inkwell.Data;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Inkwell.Infrastructure.Setup;

/// <summary>Inertia rendering, SSR supervision, shared props and health checks.</summary>
public static class InertiaSetup
{
    public static WebApplicationBuilder AddBlogInertia(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var services = builder.Services;

        services.AddValidation();
        services.AddProblemDetails();
        // Page JSON and HTML compress five to ten times (see the Inertia.Net README). The real traffic here sits behind
        // Coolify's TLS-terminating proxy (the request scheme reflects ForwardedHeaders), so compression must be enabled for HTTPS.
        services.AddResponseCompression(o => o.EnableForHttps = true);
        // The Inertia DevTools extension protocol; records in Development only by default.
        services.AddInertiaDevTools();
        services.AddInertia(o =>
        {
            // Sent once per session: the app name never changes while the tab is open.
            o.Share("appName", Once(() => config["App:Name"] ?? "Inkwell"));
            o.Share("auth", ctx => Prop(ct => SharedAuth.LoadAsync(ctx, ct)));
            // Development check: rendering a component without a matching page file throws and names the closest match.
            o.Pages.Directory = "ClientApp/pages";
            // After the page loads, prefetch at low priority the code-split chunks the next navigation will import.
            o.Vite.Prefetch = VitePrefetch.Waterfall;
            // Starts and supervises `node ssr/ssr.js` in production. Skipped while the Vite dev server runs (it does SSR
            // itself) and while the bundle is missing, so dev and non-SSR builds just render on the client.
            o.Ssr.BundlePath = "ssr/ssr.js";
            o.Ssr.Mode = SsrMode.Preferred;
            // Reuse a server-rendered page for a few minutes when the page JSON is identical (Inertia.Net 1.4). Anonymous
            // visitors share one entry per URL; later Inertia visits still fetch fresh JSON. The opt-in performance test
            // zeroes this so the benchmark keeps measuring the SSR parser, see InertiaPerformanceTests.
            var ssrCacheSeconds = config.GetValue("Inertia:SsrCacheSeconds", 300);
            if (ssrCacheSeconds < 0)
                throw new InvalidOperationException("Inertia:SsrCacheSeconds must be zero or a positive number of seconds.");
            o.Ssr.Cache.Duration = TimeSpan.FromSeconds(ssrCacheSeconds);
            // Vite may compile a page on its first request or after an edit.
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

        // Keep the native root view for public pages; private pages never contact the SSR server.
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
