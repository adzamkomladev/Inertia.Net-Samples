using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using TechLandier.Data;
using TechLandier.Features;

namespace TechLandier.Infrastructure.Setup;

public static class PipelineSetup
{
    public static WebApplication UseStorePipeline(this WebApplication app)
    {
        var config = app.Configuration;
        var o = new ForwardedHeadersOptions();

        // The proxy (Traefik on Coolify) reaches the container over a private network; trust forwarded headers only from there.
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
        foreach (var cidr in config["App:TrustedProxyNetworks"] is { Length: > 0 } nets ? nets.Split(',', StringSplitOptions.TrimEntries) : ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"])
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));

        app.UseForwardedHeaders(o);
        // After forwarded headers so the proxy's X-Forwarded-Proto makes the request count as HTTPS; browsers ignore HSTS over plain HTTP.
        if (!app.Environment.IsDevelopment()) app.UseHsts();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<RealtimeOriginMiddleware>();
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
            o.ShouldValidate = ctx => !ctx.Request.Path.StartsWithSegments("/realtime")
                && !ctx.Request.Path.StartsWithSegments("/inertia-live")
                // Runwick's management API has its own X-Runwick-Antiforgery check; sign-out is a
                // SameSite-protected one-click POST from the dashboard.
                && !ctx.Request.Path.StartsWithSegments("/runwick-api")
                && !ctx.Request.Path.StartsWithSegments("/runwick-logout")
                // Open demo dispatch endpoints: no browser session and no antiforgery cookie.
                && !ctx.Request.Path.StartsWithSegments("/demo/jobs")
                // RFC 8058 one-click unsubscribe: the mail client POSTs without a session; the signed token is the authority.
                && !ctx.Request.Path.StartsWithSegments("/email/unsubscribe");
        });
        return app;
    }
}
