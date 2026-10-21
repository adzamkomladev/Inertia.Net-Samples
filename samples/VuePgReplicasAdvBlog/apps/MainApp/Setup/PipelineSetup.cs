using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using VuePgReplicasAdvBlog.Infrastructure.Setup;
using VuePgReplicasAdvBlog.MainApp.Infrastructure;

namespace VuePgReplicasAdvBlog.MainApp.Setup;

public static class PipelineSetup
{
    public static WebApplication UseMainAppPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders(ProxySetup.TrustedProxyOptions(app.Configuration));

        app.UseMiddleware<RequestLogMiddleware>();
        if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/error");
        // Compress page JSON and HTML, then serve built assets. Before UseStaticFiles and UseInertia (README: "Compression and asset caching").
        app.UseResponseCompression();
        // Vite puts a content hash in every built file name, so the files under build/assets can be cached for good.
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = c =>
            {
                if (c.Context.Request.Path.StartsWithSegments("/build/assets"))
                    c.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            },
        });
        app.UseInertia();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        // Replaces the 1.0 UseInertiaAntiforgeryCookie() plus the hand-written unsafe-method check (removed from its old
        // slot after UseAuthentication): UseInertiaAntiforgery() issues the XSRF-TOKEN cookie on page loads and validates
        // every unsafe request, JSON bodies included. Lazy (1.4) keeps a token that still validates, so a page load no
        // longer costs a Set-Cookie and the antiforgery no-store.
        app.UseInertiaAntiforgery(o => o.CookieRefresh = XsrfCookieRefresh.Lazy);
        return app;
    }
}
