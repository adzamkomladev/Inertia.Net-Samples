using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;
using VuePgBlog.Data;
using VuePgBlog.Features;
using VuePgBlog.Features.Discovery;

namespace VuePgBlog.Infrastructure.Setup;

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
        app.UseStaticFiles();
        app.UseInertia();
        app.UseAuthentication();
        app.UseInertiaAntiforgeryCookie();
        app.UseAuthorization();
        app.UseRateLimiter();

        // Inertia submits JSON, which UseAntiforgery() skips (it only checks form-bound endpoints), so validate every unsafe request here.
        app.Use(async (ctx, next) =>
        {
            if (!ctx.Request.Path.StartsWithSegments("/realtime") && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method))
            {
                var antiforgery = ctx.RequestServices.GetRequiredService<IAntiforgery>();
                if (!await antiforgery.IsRequestValidAsync(ctx))
                {
                    ctx.Response.StatusCode = 419;
                    return;
                }
            }
            await next();
        });
        return app;
    }
}
