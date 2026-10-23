using System.ComponentModel.DataAnnotations;
using Inkwell.Data;
using Inkwell.Features.Auth;
using Inkwell.Infrastructure;
using Inertia.Net;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Features.Settings;

public sealed class SettingsService(AppDb db, TimeProvider time, IInertiaCache cache)
{
    public async Task<AppUser?> GetAsync(Guid id, CancellationToken ct) =>
        await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<Outcome> UpdateProfileAsync(ProfileInput input, Guid id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return Outcome.Missing();
        if (input.RemoveAvatar || input.AvatarUploadId is not null)
        {
            // Unmark the previous avatar so the orphan cleanup removes it after 24 h.
            await db.Uploads.Where(u => u.OwnerId == id && u.IsAvatar).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsAvatar, false), ct);
            if (input.AvatarUploadId is { } uploadId && !input.RemoveAvatar)
            {
                var upload = await db.Uploads.FirstOrDefaultAsync(u => u.Id == uploadId && u.OwnerId == id, ct);
                if (upload is null) return Outcome.Fail("avatarUploadId", "That image could not be found. Upload it again.");
                upload.IsAvatar = true;
                user.AvatarUrl = upload.Url;
            }
            else
                user.AvatarUrl = null;
        }

        user.DisplayName = input.DisplayName!.Trim();
        user.Bio = string.IsNullOrWhiteSpace(input.Bio) ? null : input.Bio.Trim();
        await db.SaveChangesAsync(ct);
        await AuthCache.InvalidateAsync(cache, id, ct);
        return Outcome.FlashBack("success", "Profile updated");
    }

    public async Task<Outcome> UpdatePreferencesAsync(PreferencesInput input, Guid id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return Outcome.Missing();
        if (!DigestSchedule.IsValidZone(input.TimeZoneId)) return Outcome.Fail("timeZoneId", "Choose an IANA time zone.");
        var p = user.Preferences;
        var rescheduled = p.TimeZoneId != input.TimeZoneId || p.DailyRecommendationDigest != input.DailyRecommendationDigest;
        p.TimeZoneId = input.TimeZoneId;
        p.DailyRecommendationDigest = input.DailyRecommendationDigest;
        p.Theme = input.Theme!;
        p.EmailOnComment = input.EmailOnComment;
        p.EmailOnLike = input.EmailOnLike;
        p.WeeklyDigest = input.WeeklyDigest;
        if (!input.DailyRecommendationDigest)
            user.NextDigestAtUtc = null;
        else if (rescheduled || user.NextDigestAtUtc is null)
            user.NextDigestAtUtc = DigestSchedule.NextDueUtc(input.TimeZoneId, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await AuthCache.InvalidateAsync(cache, id, ct);
        return Outcome.FlashBack("success", "Preferences saved");
    }
}
