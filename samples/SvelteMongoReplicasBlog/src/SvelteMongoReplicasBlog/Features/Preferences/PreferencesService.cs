using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Infrastructure;

namespace SvelteMongoReplicasBlog.Features;

public static partial class PreferencesService
{
    internal static async Task<User?> GetPreferencesAsync(
        Guid userId,
        AppDb db,
        CancellationToken ct
    )
    {
        return await db.Users.Find(u => u.Id == userId).FirstAsync(ct);
    }

    internal static async Task<Outcome> UpdatePreferencesAsync(
        PreferencesInput input,
        Guid userId,
        AppDb db,
        CancellationToken ct
    )
    {
        if (!DigestClock.IsValidIanaTimeZone(input.TimeZoneId))
            return Outcome.Fail("timeZoneId", "Choose a valid IANA time zone.");
        var user = await db.Users.Find(u => u.Id == userId).FirstAsync(ct);
        DateTime? nextDigest = user.NextDigestAtUtc;
        if (!input.DailyRecommendationDigest)
            nextDigest = null;
        else if (
            !user.Preferences.DailyRecommendationDigest
            || user.Preferences.TimeZoneId != input.TimeZoneId
            || nextDigest is null
        )
            nextDigest = DigestClock
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
        await db.Users.UpdateOneAsync(
            u => u.Id == user.Id,
            Builders<User>
                .Update.Set(u => u.Preferences, user.Preferences)
                .Set(u => u.NextDigestAtUtc, nextDigest),
            cancellationToken: ct
        );
        return Outcome.FlashBack(new Toast("success", "Preferences saved."));
    }

    internal static async Task<Outcome> UpdateProfileAsync(
        ProfileInput input,
        Guid userId,
        AppDb db,
        CancellationToken ct
    )
    {
        if (
            input.AvatarAssetId is { } assetId
            && !await db
                .Assets.Find(a =>
                    a.Id == assetId
                    && a.OwnerId == userId
                    && a.Status == AssetStatus.Ready
                    && a.ContentType.StartsWith("image/")
                )
                .AnyAsync(ct)
        )
            return Outcome.Fail("avatarAssetId", "Upload an image first.");
        await db.Users.UpdateOneAsync(
            u => u.Id == userId,
            Builders<User>
                .Update.Set(u => u.Name, input.Name!.Trim())
                .Set(u => u.AvatarAssetId, input.AvatarAssetId),
            cancellationToken: ct
        );
        return Outcome.FlashBack(new Toast("success", "Profile updated."));
    }
}
