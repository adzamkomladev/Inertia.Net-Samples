using Inertia.Net;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Infrastructure;

namespace SvelteMongoBlog.Features;

public static class Preferences
{
    public static void MapPreferences(this IEndpointRouteBuilder app)
    {
        app.MapGet("/account/profile", ShowAsync).RequireAuthorization("verified");
        app.MapPut("/account/profile", UpdateProfileAsync).RequireAuthorization("verified").WithInertiaValidation();
        app.MapGet("/account/preferences", ShowAsync).RequireAuthorization("verified");
        app.MapPut("/account/preferences", UpdatePreferencesAsync).RequireAuthorization("verified").WithInertiaValidation();
    }

    /// <summary>GET /account/profile and /account/preferences — same page component, props split by section.</summary>
    static async Task<IResult> ShowAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var user = await PreferencesService.ShowAsync(ctx.User.UserId(), db, ct);
        return Render(
            "account/Preferences",
            new InertiaProps
            {
                ["profile"] = new { name = user.Name, email = user.Email, phone = user.Phone, avatarAssetId = user.AvatarAssetId },
                ["preferences"] = user.Preferences,
            }
        );
    }

    static async Task<IResult> UpdatePreferencesAsync(PreferencesInput input, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var outcome = await PreferencesService.UpdatePreferencesAsync(input, ctx.User.UserId(), db, ct);
        if (outcome.Errors is null)
        {
            // Read by the inline script in app.html before first paint, so SSR pages don't flash the wrong theme.
            ctx.Response.Cookies.Append(
                "theme",
                input.Theme!,
                new CookieOptions
                {
                    MaxAge = TimeSpan.FromDays(365),
                    SameSite = SameSiteMode.Lax,
                    Path = "/",
                }
            );
        }
        return Respond(ctx, outcome);
    }

    static async Task<IResult> UpdateProfileAsync(ProfileInput input, HttpContext ctx, AppDb db, CancellationToken ct) =>
        Respond(ctx, await PreferencesService.UpdateProfileAsync(input, ctx.User.UserId(), db, ct));

    /// <summary>Maps a service outcome onto the Inertia contract: back+errors or back+flash.</summary>
    static IResult Respond(HttpContext ctx, Outcome outcome)
    {
        if (outcome.Errors is { } errors)
            return Back().WithErrors(errors);
        return outcome.Flash is { } toast ? Back().WithFlash("toast", toast) : Back();
    }
}
