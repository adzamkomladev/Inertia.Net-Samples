using System.Security.Cryptography;
using Inkwell.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inkwell.Infrastructure;

/// <summary>Optional mail a reader can switch off from the message itself.</summary>
public enum EmailKind { Comments, Likes, Weekly, Daily }

/// <summary>
/// Signed, expiring unsubscribe links (RFC 8058 one-click). The GET page only asks; the POST (from the page or the mail
/// client's one-click button) turns the matching preference off, so link scanners that prefetch URLs change nothing.
/// </summary>
public sealed class EmailUnsubscribe(AppDb db, IDataProtectionProvider protection, TimeProvider time, IOptions<AppOptions> app)
{
    const string Purpose = "Inkwell.Email.Unsubscribe.v2";
    // Links in daily digests sent before v2 keep working until they expire.
    const string LegacyDailyPurpose = "Inkwell.DailyRecommendations.Unsubscribe.v1";
    static readonly TimeSpan Lifetime = TimeSpan.FromDays(60);

    string BaseUrl => app.Value.BaseUrl.TrimEnd('/');

    public string SettingsUrl => $"{BaseUrl}/settings";

    public EmailFooter Footer(Guid userId, EmailKind kind) => new(SettingsUrl, Url(userId, kind), Reason(kind));

    public string Url(Guid userId, EmailKind kind)
    {
        var expires = time.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds();
        var token = protection.CreateProtector(Purpose).Protect($"{userId}|{expires}|{kind}");
        return $"{BaseUrl}/email/unsubscribe?token={Uri.EscapeDataString(token)}";
    }

    public static string Reason(EmailKind kind) => kind switch
    {
        EmailKind.Comments => "You get this because comment emails are on for your essays.",
        EmailKind.Likes => "You get this because like emails are on for your essays.",
        EmailKind.Weekly => "You get this because the weekly stats email is on.",
        _ => "You get this because daily recommendations are on.",
    };

    public static string Describe(EmailKind kind) => kind switch
    {
        EmailKind.Comments => "emails about new comments",
        EmailKind.Likes => "emails about new likes",
        EmailKind.Weekly => "the weekly stats email",
        _ => "daily recommendation emails",
    };

    /// <summary>The user and mail type a token names, or null when it is forged, malformed or expired.</summary>
    public (Guid UserId, EmailKind Kind)? Read(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        foreach (var purpose in new[] { Purpose, LegacyDailyPurpose })
        {
            try
            {
                var parts = protection.CreateProtector(purpose).Unprotect(token).Split('|');
                if (parts.Length != 3 || !Guid.TryParse(parts[0], out var userId) || !long.TryParse(parts[1], out var expires)
                    || time.GetUtcNow().ToUnixTimeSeconds() > expires) return null;
                if (purpose == LegacyDailyPurpose) return parts[2] == "daily-recommendation-digest" ? (userId, EmailKind.Daily) : null;
                return Enum.TryParse<EmailKind>(parts[2], out var kind) ? (userId, kind) : null;
            }
            catch (CryptographicException) { }
        }
        return null;
    }

    /// <summary>Turns the named preference off. Returns the kind, or null for a bad token. Idempotent.</summary>
    public async Task<EmailKind?> ApplyAsync(string? token, CancellationToken ct)
    {
        if (Read(token) is not { } named) return null;
        var (userId, kind) = named;
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return kind;
        switch (kind)
        {
            case EmailKind.Comments: user.Preferences.EmailOnComment = false; break;
            case EmailKind.Likes: user.Preferences.EmailOnLike = false; break;
            case EmailKind.Weekly: user.Preferences.WeeklyDigest = false; break;
            case EmailKind.Daily: user.Preferences.DailyRecommendationDigest = false; user.NextDigestAtUtc = null; break;
        }
        await db.SaveChangesAsync(ct);
        return kind;
    }
}
