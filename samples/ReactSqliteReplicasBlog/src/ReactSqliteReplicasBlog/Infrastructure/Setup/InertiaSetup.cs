using ReactSqliteReplicasBlog.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ReactSqliteReplicasBlog.Infrastructure.Setup;

/// <summary>Inertia rendering, SSR supervision, shared props and health checks.</summary>
public static class InertiaSetup
{
    public static WebApplicationBuilder AddBlogInertia(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var services = builder.Services;

        services.AddValidation();
        services.AddProblemDetails();
        services.AddInertia(o =>
        {
            // Sent once per session: the app name never changes while the tab is open.
            o.Share("appName", Once(() => config["App:Name"] ?? "ReactSqliteReplicasBlog"));
            o.Share("auth", ctx => Prop(ct => SharedAuth.LoadAsync(ctx, ct)));
            // Starts and supervises `node ssr/ssr.js` in production. Skipped while the Vite dev server runs (it does SSR
            // itself) and while the bundle is missing, so dev and non-SSR builds just render on the client.
            o.Ssr.BundlePath = "ssr/ssr.js";
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
