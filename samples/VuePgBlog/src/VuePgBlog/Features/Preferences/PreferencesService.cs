using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Infrastructure;

namespace VuePgBlog.Features;

public static partial class PreferencesService
{
    internal static async Task<IResult> ShowPreferencesAsync(
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == ctx.User.UserId(), ct);
        return Render(
            "Settings/Preferences",
            new InertiaProps
            {
                ["preferences"] = user.Preferences,
                ["profile"] = new { name = user.Name, avatarAssetId = user.AvatarAssetId },
            }
        );
    }

    internal static async Task<IResult> UpdatePreferencesAsync(
        PreferencesInput input,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        if (!DigestClock.IsValidIanaTimeZone(input.TimeZoneId))
            return Back()
                .WithErrors(
                    new Dictionary<string, string>
                    {
                        ["timeZoneId"] = "Choose a valid IANA time zone.",
                    }
                );
        var user = await db.Users.FirstAsync(u => u.Id == ctx.User.UserId(), ct);
        if (!input.DailyRecommendationDigest)
            user.NextDigestAtUtc = null;
        else if (
            !user.Preferences.DailyRecommendationDigest
            || user.Preferences.TimeZoneId != input.TimeZoneId
            || user.NextDigestAtUtc is null
        )
            user.NextDigestAtUtc = DigestClock
                .NextDueUtc(input.TimeZoneId, DateTimeOffset.UtcNow)
                .UtcDateTime;
        user.Preferences = new UserPreferences
        {
            Theme = input.Theme!,
            EmailOnComment = input.EmailOnComment,
            EmailOnLike = input.EmailOnLike,
            WeeklyDigest = input.WeeklyDigest,
            DailyRecommendationDigest = input.DailyRecommendationDigest,
            TimeZoneId = input.TimeZoneId,
            PostsPerPage = input.PostsPerPage,
        };
        await db.SaveChangesAsync(ct);
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
        return Back().WithFlash("toast", new Toast("success", "Preferences saved."));
    }

    internal static async Task<IResult> UpdateProfileAsync(
        ProfileInput input,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var userId = ctx.User.UserId();
        if (
            input.AvatarAssetId is { } assetId
            && !await db.Assets.AnyAsync(
                a =>
                    a.Id == assetId
                    && a.OwnerId == userId
                    && a.Status == AssetStatus.Ready
                    && a.ContentType.StartsWith("image/"),
                ct
            )
        )
            return Back()
                .WithErrors(
                    new Dictionary<string, string> { ["avatarAssetId"] = "Upload an image first." }
                );
        await db
            .Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(u => u.Name, input.Name!.Trim())
                        .SetProperty(u => u.AvatarAssetId, input.AvatarAssetId),
                ct
            );
        return Back().WithFlash("toast", new Toast("success", "Profile updated."));
    }
}
