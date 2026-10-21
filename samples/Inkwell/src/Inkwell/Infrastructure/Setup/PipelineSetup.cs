using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;

namespace Inkwell.Infrastructure.Setup;

/// <summary>The HTTP pipeline: proxy headers, logging, rendering, auth, rate limits and antiforgery.</summary>
public static class PipelineSetup
{
    public static WebApplication UseBlogPipeline(this WebApplication app)
    {
        var config = app.Configuration;

        // Coolify's Traefik terminates TLS and forwards from the Docker network; trust only proxies on the configured
        // networks (private ranges by default) so a client reaching the container directly can't spoof its IP to dodge
        // rate limits or skew analytics. Request.Scheme becomes https (Secure cookies, Back()).
        var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();
        foreach (var network in config.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>()
                     ?? ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"])
        {
            forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
        app.UseForwardedHeaders(forwarded);
        app.UseMiddleware<RealtimeOriginMiddleware>();
        app.UseMiddleware<RequestLogMiddleware>();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler(e => e.Run(ctx => ErrorPage.Render(500).ExecuteAsync(ctx)));
        }

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
        app.UseStatusCodePages(ctx => ErrorPage.HandleStatusCodeAsync(ctx.HttpContext));
        app.UseAuthentication(); // before the limiter so the "writes" policy partitions by user, not IP
        app.UseRateLimiter();
        app.UseAuthorization();

        // Replaces UseInertiaAntiforgeryCookie() plus the hand-written unsafe-method check: UseInertiaAntiforgery() issues
        // the XSRF-TOKEN cookie on page loads and validates every unsafe request, JSON bodies included (Inertia.Net 1.2+).
        // Lazy (1.4) keeps a token that still validates, so a page load no longer costs a Set-Cookie and its no-store.
        // SignalR negotiate POSTs without the token; auth pages keep the cookie-first issue the sample used before 1.2.
        app.UseInertiaAntiforgery(o =>
        {
            o.CookieRefresh = XsrfCookieRefresh.Lazy;
            o.ShouldValidate = ctx => !ctx.Request.Path.StartsWithSegments("/realtime");
            o.OnRejected = (ctx, _) =>
                ctx.Request.Headers.ContainsKey("X-Inertia")
                    ? Back().WithFlash("error", "Your session expired. Please try again.")
                    : Results.StatusCode(StatusCodes.Status400BadRequest);
        });
        return app;
    }
}
