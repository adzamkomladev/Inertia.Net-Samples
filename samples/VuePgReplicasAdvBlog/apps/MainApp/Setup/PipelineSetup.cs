using Microsoft.AspNetCore.Antiforgery;
using VuePgReplicasAdvBlog.Infrastructure.Setup;
using VuePgReplicasAdvBlog.MainApp.Infrastructure;

namespace VuePgReplicasAdvBlog.MainApp.Setup;

public static class PipelineSetup
{
    public static WebApplication UseMainAppPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders(ProxySetup.TrustedProxyOptions(app.Configuration));

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
            if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method))
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
