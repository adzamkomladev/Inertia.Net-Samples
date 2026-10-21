using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using SvelteMongoBlog.Data;

namespace SvelteMongoBlog.Infrastructure.Setup;

public static class PipelineSetup
{
    public static WebApplication UseBlogPipeline(this WebApplication app)
    {
        var config = app.Configuration;
        var o = new ForwardedHeadersOptions();

        // Traefik (Coolify) terminates TLS in front of the container and reaches it over a private Docker network.
        // Only trust forwarded headers from private ranges, so nothing else can spoof client IPs (rate limits) or the scheme.
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
        foreach (var cidr in config["App:TrustedProxyNetworks"] is { Length: > 0 } nets ? nets.Split(',', StringSplitOptions.TrimEntries) : ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"])
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));

        app.UseForwardedHeaders(o);
        app.UseMiddleware<RealtimeOriginMiddleware>();
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
        // longer costs a Set-Cookie and the antiforgery no-store. SignalR negotiate POSTs without the token.
        app.UseInertiaAntiforgery(o =>
        {
            o.CookieRefresh = XsrfCookieRefresh.Lazy;
            o.ShouldValidate = ctx => !ctx.Request.Path.StartsWithSegments("/realtime");
        });
        return app;
    }
}
