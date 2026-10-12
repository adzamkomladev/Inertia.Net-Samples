using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;

namespace ReactSqliteBlog.Infrastructure.Setup;

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

        app.UseStaticFiles();
        app.UseInertia();
        app.UseStatusCodePages(ctx => ErrorPage.HandleStatusCodeAsync(ctx.HttpContext));
        app.UseAuthentication(); // before the limiter so the "writes" policy partitions by user, not IP
        app.UseRateLimiter();
        app.UseAuthorization();
        app.UseInertiaAntiforgeryCookie();
        app.Use(async (ctx, next) =>
        {
            // Inertia posts JSON, which UseAntiforgery() does not validate, so check every unsafe method here.
            if (!ctx.Request.Path.StartsWithSegments("/realtime") && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method))
            {
                var antiforgery = ctx.RequestServices.GetRequiredService<IAntiforgery>();
                if (!await antiforgery.IsRequestValidAsync(ctx))
                {
                    if (ctx.Request.Headers.ContainsKey("X-Inertia"))
                    {
                        // Usually an expired session: reload the page so the client gets a fresh token.
                        await Back().WithFlash("error", "Your session expired. Please try again.").ExecuteAsync(ctx);
                        return;
                    }
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
            }
            await next(ctx);
        });
        return app;
    }
}
