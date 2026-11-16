using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;

namespace Inkwell.Infrastructure.Setup;

public static class PipelineSetup
{
    public static WebApplication UseBlogPipeline(this WebApplication app)
    {
        var config = app.Configuration;

        // Trust forwarded headers only from the configured networks (private ranges by default), so a client reaching
        // the container directly cannot spoof its IP to dodge rate limits.
        var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();
        foreach (var network in config.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>()
                     ?? ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"])
        {
            forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
        app.UseForwardedHeaders(forwarded);
        // Baseline headers on every response. The CSP only restricts framing, plugins, <base> and form targets: scripts and
        // styles stay unrestricted because Vite, SSR and the inline theme script would otherwise need per-request nonces.
        app.Use(async (ctx, next) =>
        {
            var h = ctx.Response.Headers;
            h.XContentTypeOptions = "nosniff";
            h.XFrameOptions = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            h.ContentSecurityPolicy = "frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self'";
            await next(ctx);
        });
        if (!app.Environment.IsDevelopment()) app.UseHsts();
        app.UseMiddleware<RealtimeOriginMiddleware>();
        app.UseMiddleware<RequestLogMiddleware>();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler(e => e.Run(async ctx => await ErrorPage.Render(500).ExecuteAsync(ctx)));
        }

        app.UseResponseCompression();
        // Vite hashes built file names, so build/assets can be cached forever.
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = c =>
            {
                if (c.Context.Request.Path.StartsWithSegments("/build/assets"))
                    c.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            },
        });
        app.UseInertia();
        app.UseStatusCodePages(async ctx => await ErrorPage.HandleStatusCodeAsync(ctx.HttpContext));
        app.UseAuthentication(); // before the limiter so the "writes" policy partitions by user, not IP
        app.UseRateLimiter();
        app.UseAuthorization();
        // Signed-in pages are encrypted in browser history; logout clears the key (ClearHistory), so Back after signing out
        // on a shared device cannot show the previous reader's inbox, drafts or settings.
        app.Use(async (ctx, next) =>
        {
            if (ctx.User.Identity?.IsAuthenticated == true) ctx.Inertia().EncryptHistory();
            await next(ctx);
        });

        // Lazy keeps a token that still validates, so page loads don't cost a Set-Cookie. SignalR negotiate POSTs without the token
        // carry no antiforgery cookie; the hubs' own origin checks stand in for CSRF on those paths.
        app.UseInertiaAntiforgery(o =>
        {
            o.CookieRefresh = XsrfCookieRefresh.Lazy;
            o.ShouldValidate = ctx => !ctx.Request.Path.StartsWithSegments("/realtime")
                && !ctx.Request.Path.StartsWithSegments("/inertia-live")
                && !ctx.Request.Path.StartsWithSegments("/runwick-api")
                && !ctx.Request.Path.StartsWithSegments("/runwick-logout")
                && !ctx.Request.Path.StartsWithSegments("/demo/jobs")
                // Mail clients' one-click unsubscribe POST has no session; the signed token in the URL is the authority.
                && !ctx.Request.Path.StartsWithSegments("/email/unsubscribe");
            o.OnRejected = (ctx, _) =>
                ctx.Request.Headers.ContainsKey("X-Inertia")
                    ? Back().WithFlash("error", "Your session expired. Please try again.")
                    : Results.StatusCode(StatusCodes.Status400BadRequest);
        });
        return app;
    }
}
