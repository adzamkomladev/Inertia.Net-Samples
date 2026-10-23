using Inkwell.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Inkwell.Features.Settings;

public static class SettingsFeature
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/settings").RequireAuthorization();
        settings.MapGet("", Show);
        settings.MapPut("/profile", async ([FromBody] ProfileInput input, SettingsService svc, HttpContext ctx, CancellationToken ct) =>
            (await svc.UpdateProfileAsync(input, ctx.User.UserId()!.Value, ct)).Respond(ctx)).WithInertiaValidation();
        settings.MapPut("/preferences", async ([FromBody] PreferencesInput input, SettingsService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var outcome = await svc.UpdatePreferencesAsync(input, ctx.User.UserId()!.Value, ct);
            if (outcome.Errors is null && !outcome.NotFound) SetThemeCookie(ctx, input.Theme!);
            return outcome.Respond(ctx);
        }).WithInertiaValidation();
        return app;
    }

    static async Task<IResult> Show(SettingsService svc, HttpContext ctx, CancellationToken ct)
    {
        var user = await svc.GetAsync(ctx.User.UserId()!.Value, ct);
        if (user is null) return ErrorPage.NotFound();
        var p = user.Preferences;
        SetThemeCookie(ctx, p.Theme);
        return Render("Settings/Index", new InertiaProps
        {
            ["profile"] = new { displayName = user.DisplayName, bio = user.Bio, email = user.Email, avatarUrl = user.AvatarUrl },
            ["preferences"] = new
            {
                theme = p.Theme,
                emailOnComment = p.EmailOnComment,
                emailOnLike = p.EmailOnLike,
                weeklyDigest = p.WeeklyDigest,
                dailyRecommendationDigest = p.DailyRecommendationDigest,
                timeZoneId = p.TimeZoneId,
            },
        });
    }

    // Readable by the client script that applies the theme before React loads.
    static void SetThemeCookie(HttpContext ctx, string theme) =>
        ctx.Response.Cookies.Append("theme", theme, new CookieOptions
        {
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            SameSite = SameSiteMode.Lax,
            HttpOnly = false,
            Secure = ctx.Request.IsHttps,
        });
}
