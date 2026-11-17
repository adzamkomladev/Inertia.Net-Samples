using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Features;

namespace TechLandier.Infrastructure;

/// <summary>Preference-backed mail a customer can switch off from the message itself. Receipts and account mail are transactional and never appear here.</summary>
public enum EmailKind { InstallmentReminders }

/// <summary>
/// Signed, expiring unsubscribe links (RFC 8058 one-click). The GET page only asks; the POST (from that page or the mail
/// client's one-click button) turns the matching preference off, so link scanners that prefetch URLs change nothing.
/// </summary>
public sealed class EmailUnsubscribe(AppDb db, IDataProtectionProvider dp, IConfiguration config)
{
    static readonly TimeSpan Lifetime = TimeSpan.FromDays(60);

    ITimeLimitedDataProtector Protector => dp.CreateProtector("TechLandier.Email.Unsubscribe.v1").ToTimeLimitedDataProtector();

    public string Url(Guid userId, EmailKind kind) =>
        $"{config.PublicBaseUrl()}/email/unsubscribe?token={Uri.EscapeDataString(Protector.Protect($"{userId}|{kind}", Lifetime))}";

    public static string Describe(EmailKind kind) => kind switch
    {
        EmailKind.InstallmentReminders => "installment reminders, payment receipts for installments and weekly statements",
        _ => "these emails",
    };

    /// <summary>The user and mail type a token names, or null when it is forged, malformed or expired.</summary>
    public (Guid UserId, EmailKind Kind)? Read(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var parts = Protector.Unprotect(token).Split('|');
            return parts.Length == 2 && Guid.TryParse(parts[0], out var userId) && Enum.TryParse<EmailKind>(parts[1], out var kind)
                ? (userId, kind)
                : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Turns the named preference off. Returns the kind, or null for a bad token. Idempotent.</summary>
    public async Task<EmailKind?> ApplyAsync(string? token, CancellationToken ct)
    {
        if (Read(token) is not { } named) return null;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == named.UserId, ct);
        if (user is null) return named.Kind;
        var p = user.Preferences;
        user.Preferences = new UserPreferences
        {
            Theme = p.Theme,
            EmailOrderUpdates = p.EmailOrderUpdates,
            EmailInstallmentReminders = false,
            EmailSupportReplies = p.EmailSupportReplies,
            TimeZoneId = p.TimeZoneId,
        };
        await db.SaveChangesAsync(ct);
        return named.Kind;
    }
}

/// <summary>Builds the <see cref="EmailContext"/> a handler renders with: the store identity plus, for notices, the user's unsubscribe link.</summary>
public sealed class EmailContexts(AppDb db, IConfiguration config, EmailUnsubscribe unsubscribe)
{
    public async Task<EmailContext> CreateAsync(CancellationToken ct, Guid? unsubscribeUserId = null, EmailKind kind = EmailKind.InstallmentReminders) =>
        new(await StoreInfo.LoadAsync(db, ct), config.PublicBaseUrl(), unsubscribeUserId is { } id ? unsubscribe.Url(id, kind) : null);
}
