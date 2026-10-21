using Microsoft.AspNetCore.Mvc;
using ReactSqliteReplicasBlog.Data;
using ReactSqliteReplicasBlog.Infrastructure;
using static ReactSqliteReplicasBlog.Features.Settings.SettingsHelpers;

namespace ReactSqliteReplicasBlog.Features.Settings;

public static class SettingsFeature
{
    public static IServiceCollection AddSettingsFeature(this IServiceCollection services) =>
        services;

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/settings").RequireAuthorization();
        g.MapGet("", ShowAsync);
        g.MapPut("/profile", UpdateProfileAsync).WithInertiaValidation();
        g.MapPut("/preferences", UpdatePreferencesAsync).WithInertiaValidation();
        return app;
    }

    static async Task<IResult> ShowAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var u = await SettingsService.GetAsync(db, ctx.User.UserId()!.Value, ct);
        if (u is null)
            return ErrorPage.NotFound();
        SetThemeCookie(ctx, u.Preferences.Theme);
        return Render(
            "Settings/Index",
            new InertiaProps
            {
                ["profile"] = new
                {
                    displayName = u.DisplayName,
                    email = u.Email,
                    avatarUrl = u.AvatarUrl,
                },
                ["preferences"] = new
                {
                    theme = u.Preferences.Theme,
                    emailOnComment = u.Preferences.EmailOnComment,
                    emailOnLike = u.Preferences.EmailOnLike,
                    weeklyDigest = u.Preferences.WeeklyDigest,
                    dailyRecommendationDigest = u.Preferences.DailyRecommendationDigest,
                    timeZoneId = u.Preferences.TimeZoneId,
                },
            }
        );
    }

    static async Task<IResult> UpdateProfileAsync(
        [FromBody] ProfileInput input,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    ) => (await SettingsService.UpdateProfileAsync(input, ctx.User.UserId()!.Value, db, ct)).Respond(ctx);

    static async Task<IResult> UpdatePreferencesAsync(
        [FromBody] PreferencesInput input,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var outcome = await SettingsService.UpdatePreferencesAsync(
            input,
            ctx.User.UserId()!.Value,
            db,
            ct
        );
        if (outcome is { Errors: null, NotFound: false, Forbidden: false })
            SetThemeCookie(ctx, input.Theme!);
        return outcome.Respond(ctx);
    }
}
