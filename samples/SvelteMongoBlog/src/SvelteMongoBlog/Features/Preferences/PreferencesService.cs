using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Infrastructure;

namespace SvelteMongoBlog.Features;

public static partial class PreferencesService
{
    /// <summary>GET /account/profile and /account/preferences — same page component, props split by section.</summary>
    internal static async Task<IResult> ShowAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var user = await db.Users.Find(u => u.Id == ctx.User.UserId()).FirstAsync(ct);
        return Render(
            "account/Preferences",
            new InertiaProps
            {
                ["profile"] = new { name = user.Name, email = user.Email, phone = user.Phone, avatarAssetId = user.AvatarAssetId },
                ["preferences"] = user.Preferences,
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
        if (input.Theme is not ("light" or "dark" or "system"))
            return Back().WithErrors(new Dictionary<string, string> { ["theme"] = "Choose light, dark or system." });

        var user = await db.Users.Find(u => u.Id == ctx.User.UserId()).FirstAsync(ct);
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
            && !await db
                .Assets.Find(a =>
                    a.Id == assetId
                    && a.OwnerId == userId
                    && a.Status == AssetStatus.Ready
                    && a.ContentType.StartsWith("image/")
                )
                .AnyAsync(ct)
        )
            return Back().WithErrors(new Dictionary<string, string> { ["avatarAssetId"] = "Upload an image first." });
        if (input.Phone is { Length: > 0 } phone && phone.Length > 24)
            return Back().WithErrors(new Dictionary<string, string> { ["phone"] = "Phone numbers are at most 24 characters." });
        await db.Users.UpdateOneAsync(
            u => u.Id == userId,
            Builders<User>
                .Update.Set(u => u.Name, input.Name!.Trim())
                .Set(u => u.Phone, string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim())
                .Set(u => u.AvatarAssetId, input.AvatarAssetId),
            cancellationToken: ct
        );
        return Back().WithFlash("toast", new Toast("success", "Profile updated."));
    }
}
