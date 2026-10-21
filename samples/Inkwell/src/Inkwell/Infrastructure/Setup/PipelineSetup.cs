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
        app.UseMiddleware<RealtimeOriginMiddleware>();
        app.UseMiddleware<RequestLogMiddleware>();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler(e => e.Run(ctx => ErrorPage.Render(500).ExecuteAsync(ctx)));
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
        app.UseStatusCodePages(ctx => ErrorPage.HandleStatusCodeAsync(ctx.HttpContext));
        app.UseAuthentication(); // before the limiter so the "writes" policy partitions by user, not IP
        app.UseRateLimiter();
        app.UseAuthorization();

        // Lazy keeps a token that still validates, so page loads don't cost a Set-Cookie. SignalR negotiate POSTs without the token.
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
