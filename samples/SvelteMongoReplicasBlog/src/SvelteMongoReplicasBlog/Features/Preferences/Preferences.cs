using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Infrastructure;

namespace SvelteMongoReplicasBlog.Features;

public static class Preferences
{
    public static void MapPreferences(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/settings").RequireAuthorization();
        group.MapGet("/", () => Results.Redirect("/settings/preferences"));
        group.MapGet("/preferences", ShowPreferencesAsync);
        group
            .MapPut("/preferences", UpdatePreferencesAsync)
            .WithInertiaValidation();
        group.MapPut("/profile", UpdateProfileAsync).WithInertiaValidation();
    }

    internal static async Task<IResult> ShowPreferencesAsync(
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var user = await db.Users.Find(u => u.Id == ctx.User.UserId()).FirstAsync(ct);
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
        var user = await db.Users.Find(u => u.Id == ctx.User.UserId()).FirstAsync(ct);
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
            return Back()
                .WithErrors(
                    new Dictionary<string, string> { ["avatarAssetId"] = "Upload an image first." }
                );
        await db.Users.UpdateOneAsync(
            u => u.Id == userId,
            Builders<User>
                .Update.Set(u => u.Name, input.Name!.Trim())
                .Set(u => u.AvatarAssetId, input.AvatarAssetId),
            cancellationToken: ct
        );
        return Back().WithFlash("toast", new Toast("success", "Profile updated."));
    }
}
