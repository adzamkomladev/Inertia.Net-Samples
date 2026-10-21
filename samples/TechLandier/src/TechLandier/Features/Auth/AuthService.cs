using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public sealed class LoginInput
{
    [Required, EmailAddress]
    public string? Email { get; set; }

    [Required]
    public string? Password { get; set; }
    public bool Remember { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class RegisterInput
{
    [Required, StringLength(80, MinimumLength = 2)]
    public string? Name { get; set; }

    [Required, EmailAddress, StringLength(254)]
    public string? Email { get; set; }

    [Required, StringLength(128, MinimumLength = 8)]
    public string? Password { get; set; }

    [Required, Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string? PasswordConfirmation { get; set; }
}

public sealed record AuthResult(SaveResult Result, User? User = null, string? AvatarKey = null);

/// Accounts: sign-in checks, registration and email verification. The cookie itself is written by the handlers (AuthHttp).
public sealed class AuthService(AppDb db, JobQueue jobs, IDataProtectionProvider dp)
{
    public const string VerifiedClaim = "email_verified";
    public const string AvatarClaim = "avatar";
    public const string ThemeClaim = "theme";

    static readonly PasswordHasher<User> Hasher = new();
    // Verified against when the email is unknown, so response time does not reveal registered emails.
    static readonly User DummyUser = new() { Email = "", Name = "" };
    static readonly string DummyHash = Hasher.HashPassword(DummyUser, "dummy-password-for-timing");

    public static ClaimsPrincipal Principal(User user, string? avatarKey)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email),
            new(VerifiedClaim, user.EmailVerifiedAt is null ? "false" : "true"),
            new(ThemeClaim, user.Preferences.Theme),
        ];
        if (user.Role == "admin") claims.Add(new(ClaimTypes.Role, "admin"));
        if (avatarKey is not null) claims.Add(new(AvatarClaim, avatarKey));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static string? SafeReturnUrl(string? url) =>
        url is { Length: > 0 } && Microsoft.AspNetCore.Http.HttpResults.RedirectHttpResult.IsLocalUrl(url) ? url : null;

    /// The verification link is a time-limited protected "userId:email" token.
    public static string VerifyUrl(IDataProtectionProvider dp, string baseUrl, User user)
    {
        var token = Protector(dp).Protect($"{user.Id}:{user.Email}", TimeSpan.FromHours(24));
        return $"{baseUrl.TrimEnd('/')}/email/verify/confirm?token={Uri.EscapeDataString(token)}";
    }

    static ITimeLimitedDataProtector Protector(IDataProtectionProvider dp) => dp.CreateProtector("email-verification").ToTimeLimitedDataProtector();

    /// A user with the asset key of their avatar, in one query. The claim holds the key (stable); SharedAuth turns it into a URL.
    public async Task<(User User, string? AvatarKey)?> FindAsync(Guid id, CancellationToken ct)
    {
        var row = await db.Users.AsNoTracking().Where(u => u.Id == id)
            .Select(u => new { User = u, AvatarKey = db.Assets.Where(a => a.Id == u.AvatarAssetId).Select(a => a.Key).FirstOrDefault() })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : (row.User, row.AvatarKey);
    }

    public async Task<AuthResult> LoginAsync(LoginInput input, CancellationToken ct)
    {
        var email = input.Email!.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        var verified = Hasher.VerifyHashedPassword(user ?? DummyUser, user?.PasswordHash ?? DummyHash, input.Password!);
        if (user is null || verified == PasswordVerificationResult.Failed)
            return new(SaveResult.Invalid("email", "These credentials do not match our records."));
        if (verified == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, input.Password!);
            await db.SaveChangesAsync(ct);
        }
        var avatarKey = user.AvatarAssetId is { } assetId
            ? await db.Assets.Where(a => a.Id == assetId).Select(a => a.Key).FirstOrDefaultAsync(ct)
            : null;
        return new(new SaveResult(RedirectUrl: SafeReturnUrl(input.ReturnUrl) ?? "/products"), user, avatarKey);
    }

    public async Task<AuthResult> RegisterAsync(RegisterInput input, string baseUrl, CancellationToken ct)
    {
        var emailTaken = new AuthResult(SaveResult.Invalid("email", "This email is already registered."));
        var email = input.Email!.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return emailTaken;
        var user = new User { Email = email, Name = input.Name!.Trim() };
        user.PasswordHash = Hasher.HashPassword(user, input.Password!);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return emailTaken; // a concurrent signup won
        }
        await jobs.EnqueueAsync(db, Emails.Verify(user.Email, user.Name, VerifyUrl(dp, baseUrl, user)), ct: ct);
        return new(new SaveResult(RedirectUrl: "/email/verify"), user);
    }

    public async Task<SaveResult> SendVerificationAsync(Guid userId, string baseUrl, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.EmailVerifiedAt is not null) return new(RedirectUrl: "/products");
        await jobs.EnqueueAsync(db, Emails.Verify(user.Email, user.Name, VerifyUrl(dp, baseUrl, user)), ct: ct);
        return SaveResult.Success("A new verification link has been sent.");
    }

    public async Task<SaveResult> ConfirmEmailAsync(string? token, bool signedIn, CancellationToken ct)
    {
        string payload;
        try { payload = Protector(dp).Unprotect(token ?? ""); }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return SaveResult.Error("That verification link is invalid or has expired.", signedIn ? "/email/verify" : "/login");
        }
        var parts = payload.Split(':', 2);
        var user = parts.Length == 2 && Guid.TryParse(parts[0], out var userId) ? await db.Users.FindAsync([userId], ct) : null;
        if (user is null || !string.Equals(user.Email, parts[1], StringComparison.OrdinalIgnoreCase))
            return SaveResult.Error("That verification link is no longer valid.", "/login");

        user.EmailVerifiedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return SaveResult.Success("Email verified. Welcome aboard!", signedIn ? "/products" : "/login");
    }
}
