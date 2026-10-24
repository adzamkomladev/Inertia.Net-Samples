using System.Security.Claims;
using CarParker.Infrastructure;

namespace CarParker.Features;

public static class Preferences
{
    public static void MapPreferences(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/account").RequireAuthorization("verified");
        account.MapGet("/profile", ShowAsync);
        account.MapGet("/preferences", ShowAsync);
        account.MapPut("/profile", async (ProfileInput input, PreferencesService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.UpdateProfileAsync(input, user.UserId(), ct)).ToResult(ctx)).WithInertiaValidation();
        account.MapPut("/preferences", UpdatePreferencesAsync).WithInertiaValidation();
        account.MapPut("/vehicles", async (VehiclesInput input, PreferencesService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.UpdateVehiclesAsync(input, user.UserId(), ct)).ToResult(ctx)).WithInertiaValidation();
    }

    // Both URLs render the same page; the props are split by section.
    static async Task<IResult> ShowAsync(PreferencesService svc, ClaimsPrincipal user, CancellationToken ct)
    {
        var u = await svc.ShowAsync(user.UserId(), ct);
        return Render("account/Preferences", new InertiaProps
        {
            ["profile"] = new { name = u.Name, email = u.Email, phone = u.Phone, avatarAssetId = u.AvatarAssetId },
            ["preferences"] = u.Preferences,
            ["vehicles"] = u.Vehicles.Select(v => new VehicleView(v.Plate, v.Label, v.IsDefault)),
        });
    }

    static async Task<IResult> UpdatePreferencesAsync(PreferencesInput input, PreferencesService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct)
    {
        var outcome = await svc.UpdatePreferencesAsync(input, user.UserId(), ct);
        // Read by the inline script in app.html before first paint, so SSR pages do not flash the wrong theme.
        ctx.Response.Cookies.Append("theme", input.Theme!, new CookieOptions { MaxAge = TimeSpan.FromDays(365), SameSite = SameSiteMode.Lax, Path = "/" });
        return outcome.ToResult(ctx);
    }
}
