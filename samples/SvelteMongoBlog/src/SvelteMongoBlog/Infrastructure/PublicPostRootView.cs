using Inertia.Net;

namespace SvelteMongoBlog.Infrastructure;

/// Decorates Inertia's SSR root view: only the public pages are server-rendered, the rest ship app.html and render on the client.
public sealed class PublicPostRootView(IInertiaRootView ssr, ViteAssets vite, IHostEnvironment env) : IInertiaRootView
{
    public async ValueTask RenderAsync(InertiaRootViewContext context)
    {
        if (context.Component is "Home" or "lots/Index" or "lots/Show")
        {
            await ssr.RenderAsync(context);
            return;
        }
        var http = context.HttpContext;
        var html = (await File.ReadAllTextAsync(Path.Combine(env.ContentRootPath, context.RootView), http.RequestAborted))
            .Replace("@inertiaHead", "")
            .Replace("@vite(\"ClientApp/app.ts\")", vite.RenderTags(http, "ClientApp/app.ts"))
            .Replace("@inertia", context.GetBodyHtml());
        await http.Response.WriteAsync(html, http.RequestAborted);
    }
}
