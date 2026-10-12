using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Preferences;
using VuePgReplicasAdvBlog.Infrastructure;
using VuePgReplicasAdvBlog.MainApp.Infrastructure;

namespace VuePgReplicasAdvBlog.MainApp.Endpoints;

public static class PreferencesEndpoints
{
    public static void MapPreferences(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/settings").RequireAuthorization();
        group.MapGet("/", () => Results.Redirect("/settings/preferences"));
        group.MapGet("/preferences", ShowAsync);
        group.MapPut("/preferences", UpdateAsync).WithInertiaValidation();
        group.MapPut("/profile", UpdateProfileAsync).WithInertiaValidation();
    }

    static async Task<IResult> ShowAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var details = await PreferencesService.ShowPreferencesAsync(ctx.User.UserId(), db, ct);
        return Render("Settings/Preferences", new InertiaProps { ["preferences"] = details.Preferences, ["profile"] = details.Profile });
    }

    static async Task<IResult> UpdateAsync(PreferencesInput input, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        await PreferencesService.UpdatePreferencesAsync(input, ctx.User.UserId(), db, ct);
        // app.html reads this before first paint to avoid a theme flash on SSR pages.
        ctx.Response.Cookies.Append("theme", input.Theme!, new CookieOptions { MaxAge = TimeSpan.FromDays(365), SameSite = SameSiteMode.Lax, Path = "/" });
        return Back().WithFlash("toast", new Toast("success", "Preferences saved."));
    }

    static async Task<IResult> UpdateProfileAsync(ProfileInput input, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        await PreferencesService.UpdateProfileAsync(input, ctx.User.UserId(), db, ct);
        return Back().WithFlash("toast", new Toast("success", "Profile updated."));
    }
}
