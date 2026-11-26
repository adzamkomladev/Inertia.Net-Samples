using Microsoft.AspNetCore.HttpOverrides;

namespace KitchenSink.Infrastructure.Setup;

public static class PipelineSetup
{
    public static WebApplication UseKitchenSinkPipeline(this WebApplication app)
    {
        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
        };
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();
        // Trust the usual private-network reverse proxy (Docker, Coolify, Traefik) and nothing else.
        foreach (var cidr in new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" })
        {
            forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
        }

        app.UseForwardedHeaders(forwarded);
        app.UseResponseCompression();
        app.UseExceptionHandler("/error");
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                if (context.Context.Request.Path.StartsWithSegments("/build/assets"))
                {
                    context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                }
            },
        });

        // The Inertia state (flash, errors, previous URL) lives in the session store, so load it first.
        app.UseSession();
        app.UseInertia();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseInertiaAntiforgery(o =>
        {
            // Always during development so tests and the browser devtools see a fresh token on every page load;
            // production keeps the cache-friendly lazy refresh.
            o.CookieRefresh = app.Environment.IsDevelopment() ? XsrfCookieRefresh.Always : XsrfCookieRefresh.Lazy;
            // HTTP QUERY is a safe, idempotent read, like GET.
            o.ShouldValidate = context => !context.Request.Path.StartsWithSegments("/inertia-live")
                && !string.Equals(context.Request.Method, "QUERY", StringComparison.OrdinalIgnoreCase);
        });
        app.UseMiddleware<GlobalRateLimitMiddleware>();

        // Real error pages for the statuses the demo shows; 418 is intentionally left unhandled.
        app.UseStatusCodePages(async context =>
        {
            var http = context.HttpContext;
            var status = http.Response.StatusCode;

            if (status is not (403 or 404 or 419 or 429 or 500 or 503)
                || http.Response.HasStarted
                || http.Request.Path.StartsWithSegments("/build")
                || !(http.Request.Headers.ContainsKey("X-Inertia")
                    || http.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            await Inertia.Net.Inertia.Render("ErrorPage", new { status })
                .WithStatusCode(status)
                .ExecuteAsync(http);
        });

        return app;
    }
}
