using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Infrastructure;

namespace SvelteMongoBlog.Features;

public static partial class PreferencesService
{
    /// <summary>Loads the signed-in user for /account/profile and /account/preferences; the route handler splits the props.</summary>
    internal static async Task<User> ShowAsync(Guid userId, AppDb db, CancellationToken ct) =>
        await db.Users.Find(u => u.Id == userId).FirstAsync(ct);

    internal static async Task<Outcome> UpdatePreferencesAsync(
        PreferencesInput input,
        Guid userId,
        AppDb db,
        CancellationToken ct
    )
    {
        if (input.Theme is not ("light" or "dark" or "system"))
            return Outcome.Fail("theme", "Choose light, dark or system.");

        var user = await db.Users.Find(u => u.Id == userId).FirstAsync(ct);
        user.Preferences = new UserPreferences
        {
            Theme = input.Theme!,
            BookingReminders = input.BookingReminders,
            EmailOnCheckout = input.EmailOnCheckout,
            EmailOnRefund = input.EmailOnRefund,
        };
        await db.Users.UpdateOneAsync(
            u => u.Id == user.Id,
            Builders<User>.Update.Set(u => u.Preferences, user.Preferences),
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
        if (input.Phone is { Length: > 0 } phone && phone.Length > 24)
            return Outcome.Fail("phone", "Phone numbers are at most 24 characters.");
        await db.Users.UpdateOneAsync(
            u => u.Id == userId,
            Builders<User>
                .Update.Set(u => u.Name, input.Name!.Trim())
                .Set(u => u.Phone, string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim())
                .Set(u => u.AvatarAssetId, input.AvatarAssetId),
            cancellationToken: ct
        );
        return Outcome.FlashBack(new Toast("success", "Profile updated."));
    }
}
