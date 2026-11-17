using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using TechLandier.Infrastructure;

namespace TechLandier.Infrastructure.Setup;

public static class PipelineSetup
{
    public static WebApplication UseStorePipeline(this WebApplication app)
    {
        app.UseForwardedHeaders(ProxySetup.TrustedProxyOptions(app.Configuration));
        // After forwarded headers so the proxy's X-Forwarded-Proto makes the request count as HTTPS; browsers ignore HSTS over plain HTTP.
        if (!app.Environment.IsDevelopment()) app.UseHsts();
        app.UseMiddleware<SecurityHeadersMiddleware>();
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

        // Signed-in account, admin and checkout pages are encrypted in browser history; logout clears the key (ClearHistory),
        // so Back on a shared device cannot show the previous user's orders, tickets or admin data.
        app.Use(async (ctx, next) =>
        {
            if (ctx.User.Identity?.IsAuthenticated == true
                && (ctx.Request.Path.StartsWithSegments("/account") || ctx.Request.Path.StartsWithSegments("/admin") || ctx.Request.Path.StartsWithSegments("/checkout")))
                ctx.Inertia().EncryptHistory();
            await next(ctx);
        });

        // Issues the XSRF-TOKEN cookie on page loads and validates every unsafe request. Lazy keeps a token that still
        // validates; SignalR negotiate POSTs without one.
        app.UseInertiaAntiforgery(o =>
        {
            o.CookieRefresh = XsrfCookieRefresh.Lazy;
            // Open demo dispatch endpoints have no browser session and no antiforgery cookie. The hubs and the Runwick
            // dashboard live in other roles.
            o.ShouldValidate = ctx => !ctx.Request.Path.StartsWithSegments("/demo/jobs")
                // RFC 8058 one-click unsubscribe: the mail client POSTs without a session; the signed token is the authority.
                && !ctx.Request.Path.StartsWithSegments("/email/unsubscribe");
        });
        return app;
    }
}
