using Inertia.Net;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Features;
using SvelteMongoReplicasBlog.Features.Discovery;

namespace SvelteMongoReplicasBlog.Infrastructure.Setup;

public static class InertiaSetup
{
    public static WebApplicationBuilder AddBlogInertia(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        builder.Services.AddValidation();
        builder.Services.AddProblemDetails();
        builder.Services.AddInertia(o =>
        {
            o.Share("appName", Once(() => config["App:Name"] ?? "Inkwell"));
            o.Share("auth", AuthService.SharedAuth);
            o.Ssr.Enabled = config.GetValue("Inertia:Ssr", true);
            // Vite may compile a page on its first dev request, so Development gets a longer timeout.
            var timeout = config.GetValue<double>("Inertia:SsrTimeoutSeconds", builder.Environment.IsDevelopment() ? 30 : 5);
            if (!double.IsFinite(timeout) || timeout <= 0) throw new InvalidOperationException("SSR_TIMEOUT_SECONDS must be a positive, finite number.");
            o.Ssr.Timeout = TimeSpan.FromSeconds(timeout);
            // The SSR bundle is plain JS; bun runs it like node (bundle path stays ssr/ssr.js).
            if (o.Ssr.Enabled) o.Ssr.UseNodeProcess(n => n.Executable = config["Inertia:SsrExecutable"] ?? "bun");
        });

        // SSR only the public pages; everything else renders client-side from app.html and never contacts the SSR process.
        var rootView = builder.Services.Single(d => d.ServiceType == typeof(IInertiaRootView)).ImplementationType!;
        builder.Services.AddSingleton<IInertiaRootView>(sp => new PublicPostRootView(
            (IInertiaRootView)ActivatorUtilities.CreateInstance(sp, rootView),
            sp.GetRequiredService<ViteAssets>(), sp.GetRequiredService<IHostEnvironment>()));

        var health = builder.Services.AddHealthChecks().AddCheck<MongoHealthCheck>("mongodb");
        if (config.GetValue("Inertia:Ssr", true)) health.AddInertiaSsr();
        return builder;
    }
}
