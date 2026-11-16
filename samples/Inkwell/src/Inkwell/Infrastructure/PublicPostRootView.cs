using Inertia.Net;

namespace Inkwell.Infrastructure;

// Use Inertia's SSR root view only for the public feed and post detail components.
public sealed class PublicPostRootView(IInertiaRootView serverView, ViteAssets vite, IHostEnvironment environment) : IInertiaRootView
{
    private const string ShellStart = "<template id=\"app-shell\">";
    private const string ShellEnd = "</template>";

    // app.html is parsed once outside Development; the dev server re-reads it so edits show without a restart.
    private ClientShell? cached;

    public static bool UsesSsr(string component) => component is "Posts/Index" or "Posts/Show";

    public async ValueTask RenderAsync(InertiaRootViewContext context)
    {
        if (UsesSsr(context.Component))
        {
            await serverView.RenderAsync(context);
            return;
        }

        var http = context.HttpContext;
        var shell = cached ?? await LoadAsync(context.RootView, http.RequestAborted);
        if (!environment.IsDevelopment()) cached = shell;

        // The page JSON and an empty mount node; the skeleton goes inside the node, where React's first render
        // replaces it. Inertia only hydrates nodes marked data-server-rendered, so this stays a plain client render.
        var body = context.GetBodyHtml();
        var mountEnd = body.LastIndexOf("</div>", StringComparison.Ordinal);
        var html = string.Concat(
            shell.BeforeRefresh,
            vite.RenderReactRefresh(http),
            shell.BeforeVite,
            vite.RenderTags(http, "ClientApp/app.tsx"),
            shell.BeforeBody,
            mountEnd < 0 ? body : string.Concat(body.AsSpan(0, mountEnd), shell.Skeleton, body.AsSpan(mountEnd)),
            shell.AfterBody);
        await http.Response.WriteAsync(html, http.RequestAborted);
    }

    private async Task<ClientShell> LoadAsync(string rootView, CancellationToken token)
    {
        var html = await File.ReadAllTextAsync(Path.Combine(environment.ContentRootPath, rootView), token);
        // The template is only needed as the skeleton source; strip it so client renders do not carry it twice.
        var skeleton = "";
        var start = html.IndexOf(ShellStart, StringComparison.Ordinal);
        if (start >= 0)
        {
            var end = html.IndexOf(ShellEnd, start, StringComparison.Ordinal);
            skeleton = html[(start + ShellStart.Length)..end].Trim();
            html = html.Remove(start, end + ShellEnd.Length - start);
        }

        // Server renders get their <title> from the page's <Head>; client renders show the app name until boot.
        html = html.Replace("@inertiaHead", "<title>Inkwell</title>");
        var parts = html.Split(["@viteReactRefresh", "@vite(\"ClientApp/app.tsx\")", "@inertia"], StringSplitOptions.None);
        if (parts.Length != 4)
            throw new InvalidOperationException($"{rootView} must contain @viteReactRefresh, @vite(\"ClientApp/app.tsx\") and @inertia once each.");
        return new ClientShell(parts[0], parts[1], parts[2], parts[3], skeleton);
    }

    private sealed record ClientShell(string BeforeRefresh, string BeforeVite, string BeforeBody, string AfterBody, string Skeleton);
}
