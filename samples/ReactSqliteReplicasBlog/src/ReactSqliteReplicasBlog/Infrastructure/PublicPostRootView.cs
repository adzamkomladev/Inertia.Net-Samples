using Inertia.Net;

namespace ReactSqliteReplicasBlog.Infrastructure;

// Use Inertia's SSR root view only for the public feed and post detail components.
public sealed class PublicPostRootView(IInertiaRootView serverView, ViteAssets vite, IHostEnvironment environment) : IInertiaRootView
{
    public static bool UsesSsr(string component) => component is "Posts/Index" or "Posts/Show";

    public async ValueTask RenderAsync(InertiaRootViewContext context)
    {
        if (UsesSsr(context.Component))
        {
            await serverView.RenderAsync(context);
            return;
        }

        // The sample's app.html has these four tokens; reuse its theme script and document shell.
        var html = await File.ReadAllTextAsync(Path.Combine(environment.ContentRootPath, context.RootView), context.HttpContext.RequestAborted);
        html = html.Replace("@inertiaHead", "")
            .Replace("@viteReactRefresh", vite.RenderReactRefresh(context.HttpContext))
            .Replace("@vite(\"ClientApp/app.tsx\")", vite.RenderTags(context.HttpContext, "ClientApp/app.tsx"))
            .Replace("@inertia", context.GetBodyHtml());
        await context.HttpContext.Response.WriteAsync(html, context.HttpContext.RequestAborted);
    }
}
