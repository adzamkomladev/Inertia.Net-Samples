using Microsoft.AspNetCore.Antiforgery;

namespace CarParker.Infrastructure;

/// <summary>
/// A POST that renders the next page inline (performance playbook: "return the next page from the POST") needs the
/// page object's <c>url</c> to be the address the browser would have visited — the GET url, not the POST path — so the
/// SPA history entry, prop-reload keys and subsequent visits stay correct. Routes call <see cref="Next"/> right before
/// returning the next page's render; <see cref="Resolve"/> is wired as <c>InertiaOptions.UrlResolver</c> and falls back
/// to Inertia's default (PathBase + Path + QueryString).
/// </summary>
public static class PageUrl
{
    const string Key = "CarParker.PageUrl";

    public static void Next(HttpContext ctx, string url) => ctx.Items[Key] = url;

    /// <summary>
    /// Re-issues the script-readable antiforgery cookie for the request's current identity. An inline render after a
    /// sign-in or sign-out must carry it: the token the browser holds was minted for the previous identity, and the
    /// follow-up page load that used to refresh the cookie no longer happens. Same cookie as
    /// <c>UseInertiaAntiforgery</c> writes on page loads (its default name and essentiality).
    /// </summary>
    public static void RefreshXsrf(HttpContext ctx)
    {
        var antiforgery = ctx.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(ctx);
        ctx.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Secure = ctx.Request.IsHttps,
            Path = "/",
            // Like the antiforgery cookie itself: a consent policy must not withhold it, or every form submission fails.
            IsEssential = true,
        });
    }

    public static string Resolve(HttpContext ctx) =>
        ctx.Items.TryGetValue(Key, out var value) && value is string url ? url : Default(ctx);

    static string Default(HttpContext ctx)
    {
        var pathBase = ctx.Request.PathBase.ToUriComponent();
        var path = ctx.Request.Path.ToUriComponent();
        var query = ctx.Request.QueryString.ToUriComponent();
        return pathBase.Length == 0 && path.Length == 0 && query.Length == 0 ? "/" : $"{pathBase}{path}{query}";
    }
}
