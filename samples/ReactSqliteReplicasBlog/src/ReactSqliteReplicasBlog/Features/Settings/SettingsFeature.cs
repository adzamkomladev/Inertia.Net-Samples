using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    internal static async Task<IResult> ShowAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var id = ctx.User.UserId()!.Value;
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
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

    internal static async Task<IResult> UpdateProfileAsync(
        HttpContext ctx,
        [FromBody] ProfileInput input,
        AppDb db,
        CancellationToken ct
    )
    {
        var id = ctx.User.UserId()!.Value;
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null)
            return ErrorPage.NotFound();
        if (input.RemoveAvatar || input.AvatarUploadId is not null)
        {
            // Unmark the previous avatar so the orphan cleanup removes it after 24 h.
            foreach (
                var old in await db
                    .Uploads.Where(x => x.OwnerId == id && x.IsAvatar)
                    .ToListAsync(ct)
            )
                old.IsAvatar = false;
            if (input.AvatarUploadId is { } uploadId && !input.RemoveAvatar)
            {
                var upload = await db.Uploads.FirstOrDefaultAsync(
                    x => x.Id == uploadId && x.OwnerId == id,
                    ct
                );
                if (upload is null)
                    return Back()
                        .WithErrors(
                            new Dictionary<string, string>
                            {
                                ["avatarUploadId"] =
                                    "That image could not be found. Upload it again.",
                            }
                        );
                upload.IsAvatar = true;
                user.AvatarUrl = upload.Url;
            }
            else
                user.AvatarUrl = null;
        }

        user.DisplayName = input.DisplayName!.Trim();
        await db.SaveChangesAsync(ct);
        return Back().WithFlash("success", "Profile updated");
    }

    internal static async Task<IResult> UpdatePreferencesAsync(
        HttpContext ctx,
        [FromBody] PreferencesInput input,
        AppDb db,
        CancellationToken ct
    )
    {
        var id = ctx.User.UserId()!.Value;
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null)
            return ErrorPage.NotFound();
        if (!DigestClock.IsValidIanaTimeZone(input.TimeZoneId))
            return Back()
                .WithErrors(
                    new Dictionary<string, string> { ["timeZoneId"] = "Choose an IANA time zone." }
                );
        var changedSchedule =
            user.Preferences.TimeZoneId != input.TimeZoneId
            || user.Preferences.DailyRecommendationDigest != input.DailyRecommendationDigest;
        user.Preferences.TimeZoneId = input.TimeZoneId;
        user.Preferences.DailyRecommendationDigest = input.DailyRecommendationDigest;
        if (!input.DailyRecommendationDigest)
            user.NextDigestAtUtc = null;
        else if (changedSchedule || user.NextDigestAtUtc == null)
            user.NextDigestAtUtc = DigestClock
                .NextDueUtc(input.TimeZoneId, DateTimeOffset.UtcNow)
                .UtcDateTime;
        user.Preferences.Theme = input.Theme!;
        user.Preferences.EmailOnComment = input.EmailOnComment;
        user.Preferences.EmailOnLike = input.EmailOnLike;
        user.Preferences.WeeklyDigest = input.WeeklyDigest;
        await db.SaveChangesAsync(ct);
        SetThemeCookie(ctx, input.Theme!);
        return Back().WithFlash("success", "Preferences saved");
    }
}
