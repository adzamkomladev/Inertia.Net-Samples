using Inertia.Net;

namespace TechLandier.Infrastructure;

/// Decorates Inertia's SSR root view: only public storefront pages are server-rendered,
/// account/admin ship app.html and render on the client. Client-rendered pages get an inline placeholder shell
/// inside the mount node (replaced when Vue mounts), so the first paint is never blank.
public sealed class StoreRootView(IInertiaRootView ssr, ViteAssets vite, IHostEnvironment env) : IInertiaRootView
{
    static readonly HashSet<string> ServerRendered =
        ["Home", "products/Index", "products/Show", "deals/Index", "pages/About", "pages/Policy", "Auth/Login", "Auth/Register"];

    // app.html is read once and re-read only when the file changes (so edits still show up in development).
    readonly Lock gate = new();
    string? template;
    DateTime templateStamp;

    public async ValueTask RenderAsync(InertiaRootViewContext context)
    {
        if (ServerRendered.Contains(context.Component))
        {
            await ssr.RenderAsync(context);
            return;
        }
        var http = context.HttpContext;
        var body = context.GetBodyHtml();
        var mount = $"<div id=\"{context.RootElementId}\"></div>";
        body = body.Replace(mount, $"<div id=\"{context.RootElementId}\">{Shell(context.Component)}</div>");
        var html = Template(context.RootView)
            .Replace("@inertiaHead", "")
            .Replace("@vite(\"ClientApp/app.ts\")", vite.RenderTags(http, "ClientApp/app.ts"))
            .Replace("@inertia", body);
        await http.Response.WriteAsync(html, http.RequestAborted);
    }

    string Template(string rootView)
    {
        var path = Path.Combine(env.ContentRootPath, rootView);
        var stamp = File.GetLastWriteTimeUtc(path);
        lock (gate)
        {
            if (template is null || stamp != templateStamp)
            {
                template = File.ReadAllText(path);
                templateStamp = stamp;
            }
            return template;
        }
    }
    static string Shell(string component) =>
        component.StartsWith("admin/") || component.StartsWith("account/") ? SidebarShell
        : component.StartsWith("Auth/") ? AuthShell
        : StoreShell;

    const string StoreShell =
        """
        <div class="shell"><header><div class="sk" style="width:128px;height:28px"></div><div class="sk" style="margin-left:auto;width:96px;height:32px"></div></header>
        <div class="wrap"><div class="sk" style="height:40px;width:50%"></div><div class="grid">
        <div class="sk" style="aspect-ratio:1;border-radius:12px"></div><div class="sk" style="aspect-ratio:1;border-radius:12px"></div>
        <div class="sk" style="aspect-ratio:1;border-radius:12px"></div><div class="sk" style="aspect-ratio:1;border-radius:12px"></div></div></div></div>
        """;

    const string SidebarShell =
        """
        <div class="shell sidebar"><aside class="side"><div class="sk" style="height:28px;width:128px;margin-bottom:24px"></div>
        <div class="sk" style="height:36px;margin-bottom:8px"></div><div class="sk" style="height:36px;margin-bottom:8px"></div><div class="sk" style="height:36px"></div></aside>
        <div class="main"><header><div class="sk" style="width:112px;height:28px"></div><div class="sk" style="margin-left:auto;width:96px;height:32px"></div></header>
        <div class="wrap"><div class="sk" style="height:36px;width:40%"></div><div class="sk" style="height:288px;border-radius:16px"></div></div></div></div>
        """;

    const string AuthShell =
        """
        <div class="shell"><div class="wrap" style="max-width:24rem;padding-top:96px"><div class="sk" style="height:28px;width:128px"></div>
        <div class="sk" style="height:40px"></div><div class="sk" style="height:40px"></div><div class="sk" style="height:44px"></div></div></div>
        """;
}
