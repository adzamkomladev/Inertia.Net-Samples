using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;

namespace VuePgBlog.Infrastructure;

/// <summary>Anonymous, signed unsubscribe endpoint scoped to the promo email preference.</summary>
public static class PromoUnsubscribe
{
    const string UnsubscribePurpose = "VuePgBlog.PromoMail.Unsubscribe.v1";

    /// <summary>Expires in 180 days like the original digest link; routes into EmailPromos = false.</summary>
    public static void Map(IEndpointRouteBuilder app) => app.MapGet("/email/unsubscribe/promo",
        async Task<IResult> (string? token, IDataProtectionProvider protection, AppDb db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(token)) return Results.BadRequest("Invalid unsubscribe link.");
            try
            {
                var payload = protection.CreateProtector(UnsubscribePurpose).Unprotect(token);
                var parts = payload.Split('|');
                if (parts.Length != 3
                    || !Guid.TryParse(parts[0], out var userId) || userId == Guid.Empty
                    || !long.TryParse(parts[1], out var expiresAt)
                    || parts[2] != "promo-mail"
                    || DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresAt)
                    return Results.BadRequest("This unsubscribe link is invalid or expired.");

                var user = await db.Users.SingleOrDefaultAsync(row => row.Id == userId, ct);
                if (user is not null)
                {
                    user.Preferences.EmailPromos = false;
                    await db.SaveChangesAsync(ct);
                }
                return Results.Content("<!doctype html><html><body style=\"font-family:system-ui,sans-serif\"><h1>You're unsubscribed</h1><p>TechLandier promo emails are turned off. Order and installment emails keep flowing to your inbox.</p></body></html>", "text/html");
            }
            catch (CryptographicException) { return Results.BadRequest("This unsubscribe link is invalid or expired."); }
            catch (FormatException) { return Results.BadRequest("This unsubscribe link is invalid or expired."); }
        }).AllowAnonymous();

    public static string Url(IDataProtectionProvider dp, string baseUrl, Guid userId)
    {
        var expiresAt = DateTime.UtcNow.AddDays(180).ToUnixTimeSeconds();
        var token = dp.CreateProtector(UnsubscribePurpose).Protect($"{userId}|{expiresAt}|promo-mail");
        return $"{baseUrl.TrimEnd('/')}/email/unsubscribe/promo?token={Uri.EscapeDataString(token)}";
    }
}
