using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using TechLandier.Infrastructure;

namespace TechLandier.Infrastructure.Setup;

public static class PipelineSetup
{
    public static WebApplication UseStorePipeline(this WebApplication app)
    {
        app.UseForwardedHeaders(ProxySetup.TrustedProxyOptions(app.Configuration));
        app.UseMiddleware<RequestLogMiddleware>();
        if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/error");
        app.UseResponseCompression();
        // Vite hashes built file names, so build/assets can be cached for good.
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

        // Issues the XSRF-TOKEN cookie on page loads and validates every unsafe request.
        app.UseInertiaAntiforgery(o => o.CookieRefresh = XsrfCookieRefresh.Lazy);
        return app;
    }
}
