using System.ComponentModel.DataAnnotations;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;

namespace CarParker.Features;

public sealed class PreferencesService(AppDb db)
{
    public async Task<User> ShowAsync(Guid userId, CancellationToken ct) =>
        await db.Users.Find(u => u.Id == userId).FirstAsync(ct);

    public async Task<Outcome> UpdatePreferencesAsync(PreferencesInput input, Guid userId, CancellationToken ct)
    {
        await db.Users.UpdateOneAsync(u => u.Id == userId,
            Builders<User>.Update.Set(u => u.Preferences, new UserPreferences
            {
                Theme = input.Theme!,
                BookingReminders = input.BookingReminders,
                EmailOnCheckout = input.EmailOnCheckout,
                EmailOnRefund = input.EmailOnRefund,
            }), cancellationToken: ct);
        return Outcome.FlashBack(new Toast("success", "Preferences saved."));
    }

    public async Task<Outcome> UpdateProfileAsync(ProfileInput input, Guid userId, CancellationToken ct)
    {
        if (input.AvatarAssetId is { } assetId
            && !await db.Assets.Find(a => a.Id == assetId && a.OwnerId == userId && a.Status == AssetStatus.Ready && a.ContentType.StartsWith("image/")).AnyAsync(ct))
            return Outcome.Fail("avatarAssetId", "Upload an image first.");
        await db.Users.UpdateOneAsync(u => u.Id == userId,
            Builders<User>.Update.Set(u => u.Name, input.Name!.Trim())
                .Set(u => u.Phone, string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim())
                .Set(u => u.AvatarAssetId, input.AvatarAssetId),
            cancellationToken: ct);
        return Outcome.FlashBack(new Toast("success", "Profile updated."));
    }
}
