using ReactSqliteBlog.Data;
using ReactSqliteBlog.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static ReactSqliteBlog.Features.Settings.SettingsHelpers;

namespace ReactSqliteBlog.Features.Settings;

public static partial class SettingsService
{
    /// <summary>The signed-in user for the settings page, or null when the session no longer resolves.</summary>
    internal static async Task<AppUser?> GetAsync(AppDb db, Guid id, CancellationToken ct) =>
        await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    internal static async Task<Outcome> UpdateProfileAsync(
        ProfileInput input,
        Guid id,
        AppDb db,
        CancellationToken ct
    )
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null)
            return Outcome.Missing();
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
                    return Outcome.Fail(
                        "avatarUploadId",
                        "That image could not be found. Upload it again."
                    );
                upload.IsAvatar = true;
                user.AvatarUrl = upload.Url;
            }
            else
                user.AvatarUrl = null;
        }

        user.DisplayName = input.DisplayName!.Trim();
        await db.SaveChangesAsync(ct);
        return Outcome.FlashBack("success", "Profile updated");
    }

    internal static async Task<Outcome> UpdatePreferencesAsync(
        PreferencesInput input,
        Guid id,
        AppDb db,
        CancellationToken ct
    )
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null)
            return Outcome.Missing();
        if (!DigestClock.IsValidIanaTimeZone(input.TimeZoneId))
            return Outcome.Fail("timeZoneId", "Choose an IANA time zone.");
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
        return Outcome.FlashBack("success", "Preferences saved");
    }
}
