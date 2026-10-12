using Microsoft.EntityFrameworkCore;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Features.Preferences;

public static class PreferencesService
{
    public static async Task<PreferenceDetails> ShowPreferencesAsync(
        Guid userId,
        AppDb db,
        CancellationToken ct
    )
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
        return new PreferenceDetails(user.Preferences, new ProfileDetails(user.Name, user.AvatarAssetId));
    }

    public static async Task UpdatePreferencesAsync(
        PreferencesInput input,
        Guid userId,
        AppDb db,
        CancellationToken ct
    )
    {
        if (!DigestClock.IsValidIanaTimeZone(input.TimeZoneId))
            throw FeatureException.Invalid("timeZoneId", "Choose a valid IANA time zone.");
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
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
    }

    public static async Task UpdateProfileAsync(
        ProfileInput input,
        Guid userId,
        AppDb db,
        CancellationToken ct
    )
    {
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
            throw FeatureException.Invalid("avatarAssetId", "Upload an image first.");
        await db
            .Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(u => u.Name, input.Name!.Trim())
                        .SetProperty(u => u.AvatarAssetId, input.AvatarAssetId),
                ct
            );
    }
}
