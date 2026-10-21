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

        // Issues the XSRF-TOKEN cookie on page loads and validates every unsafe request. Lazy keeps a token that still
        // validates; SignalR negotiate POSTs without one.
        app.UseInertiaAntiforgery(o =>
        {
            o.CookieRefresh = XsrfCookieRefresh.Lazy;
            o.ShouldValidate = ctx => !ctx.Request.Path.StartsWithSegments("/realtime");
        });
        return app;
    }
}
