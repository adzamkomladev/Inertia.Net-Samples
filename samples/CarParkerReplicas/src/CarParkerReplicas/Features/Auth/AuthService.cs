using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;

namespace CarParker.Features;

public sealed class AuthService(AppDb db, JobEnqueuer jobs, PrincipalCache principals, IDataProtectionProvider dp, ILogger<AuthService> log)
{
    static readonly PasswordHasher<User> Hasher = new();
    // Verified against when the email is unknown so response time doesn't reveal registered emails.
    static readonly User DummyUser = new() { Email = "", Name = "" };
    static readonly string DummyHash = Hasher.HashPassword(DummyUser, "dummy-password-for-timing");

    static string Normalize(string email) => email.Trim().ToLowerInvariant();

    ITimeLimitedDataProtector Protector => dp.CreateProtector("email-verification").ToTimeLimitedDataProtector();

    /// <summary>The 24-hour confirmation link; built by the verification job when it runs.</summary>
    internal static string VerifyUrl(IDataProtectionProvider dp, string baseUrl, User user) =>
        $"{baseUrl}/email/verify/confirm?token={Uri.EscapeDataString(dp.CreateProtector("email-verification").ToTimeLimitedDataProtector().Protect($"{user.Id}:{user.Email}", TimeSpan.FromHours(24)))}";

    /// <summary>The principal for a stored user with fresh claims, or null when the user no longer exists. The avatar claim holds the
    /// asset key (stable); shared props turn it into a URL, since presigned URLs change on every request.</summary>
    public async Task<AuthAccount?> FindAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        return user is null ? null : await AccountAsync(user, ct);
    }

    async Task<AuthAccount> AccountAsync(User user, CancellationToken ct) =>
        new(user, user.AvatarAssetId is { } assetId
            ? await db.Assets.Find(a => a.Id == assetId).Project(a => a.Key).FirstOrDefaultAsync(ct)
            : null);

    // The per-address rate limit is sized for a whole room behind one NAT (playbook), so the account itself stays the
    // abuse boundary for anonymous sign-ins: this in-process window throttles attempts per submitted address. It is
    // process-local by design (CarParkerReplicas: three replicas each hold their own window; the per-address limiter
    // still bounds the room, so the combined effect stays bounded).
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTime WindowStart)> Attempts = new();
    const int MaxAttemptsPerAccount = 30;
    /// <summary>Exposed for the rate-limit test, which walks the whole window.</summary>
    public const int AttemptsPerWindow = MaxAttemptsPerAccount;
    static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(5);

    public async Task<(AuthAccount? Account, string? Guarded)> LoginAsync(LoginInput input, CancellationToken ct)
    {
        var email = Normalize(input.Email!);
        if (!AllowAttempt(email)) return (null, "Too many sign-in attempts for this account. Try again in a few minutes.");
        var user = await db.Users.Find(u => u.Email == email).FirstOrDefaultAsync(ct);
        var result = Hasher.VerifyHashedPassword(user ?? DummyUser, user?.PasswordHash ?? DummyHash, input.Password!);
        if (user is null || result == PasswordVerificationResult.Failed) return (null, null);
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            await db.Users.UpdateOneAsync(u => u.Id == user.Id,
                Builders<User>.Update.Set(u => u.PasswordHash, Hasher.HashPassword(user, input.Password!)), cancellationToken: ct);
        return (await AccountAsync(user, ct), null);
    }

    /// <summary>A sliding fixed window per account: <see cref="MaxAttemptsPerAccount"/> attempts per <see cref="AttemptWindow"/>.</summary>
    static bool AllowAttempt(string email)
    {
        var now = DateTime.UtcNow;
        while (true)
        {
            if (Attempts.TryAdd(email, (1, now))) return true;
            var (count, start) = Attempts[email];
            if (now - start >= AttemptWindow)
            {
                if (Attempts.TryUpdate(email, (1, now), (count, start))) return true;
                continue;
            }
            if (count >= MaxAttemptsPerAccount) return false;
            if (Attempts.TryUpdate(email, (count + 1, start), (count, start))) return true;
        }
    }

    public async Task<RegisterResult> RegisterAsync(RegisterInput input, CancellationToken ct)
    {
        var user = new User { Email = Normalize(input.Email!), Name = input.Name!.Trim(), Role = UserRole.Customer };
        user.PasswordHash = Hasher.HashPassword(user, input.Password!);
        try { await db.Users.InsertOneAsync(user, cancellationToken: ct); }
        catch (MongoException e) when (e is MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey } or MongoCommandException { Code: 11000 })
        {
            return new RegisterResult(Outcome.Fail("email", "This email is already registered."), null);
        }
        // The user row is committed; the job carries only the id and mints its own link.
        await jobs.DispatchAsync(new SendVerificationEmail(user.Id), ct);
        return new RegisterResult(Outcome.RedirectTo("/email/verify"), new AuthAccount(user, null));
    }

    public async Task<Outcome> SendVerificationAsync(Guid userId, CancellationToken ct)
    {
        var verifiedAt = await db.Users.Find(u => u.Id == userId).Project(u => new { u.EmailVerifiedAt }).FirstOrDefaultAsync(ct);
        if (verifiedAt is null || verifiedAt.EmailVerifiedAt is not null) return Outcome.RedirectTo("/lots");
        await jobs.DispatchAsync(new SendVerificationEmail(userId), ct);
        return Outcome.FlashBack(new Toast("success", "A new verification link has been sent."));
    }
    public async Task<Outcome> ConfirmEmailAsync(string? token, bool authenticated, CancellationToken ct)
    {
        string payload;
        try { payload = Protector.Unprotect(token ?? ""); }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return Outcome.FlashThenRedirect(new Toast("error", "That verification link is invalid or has expired."), authenticated ? "/email/verify" : "/login");
        }

        var parts = payload.Split(':', 2);
        var user = parts.Length == 2 && Guid.TryParse(parts[0], out var userId)
            ? await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct)
            : null;
        if (user is null || !string.Equals(user.Email, parts[^1], StringComparison.OrdinalIgnoreCase))
            return Outcome.FlashThenRedirect(new Toast("error", "That verification link is no longer valid."), "/login");

        if (user.EmailVerifiedAt is null)
        {
            await db.Users.UpdateOneAsync(u => u.Id == user.Id && u.Email == user.Email && u.EmailVerifiedAt == null,
                Builders<User>.Update.Set(u => u.EmailVerifiedAt, DateTime.UtcNow), cancellationToken: ct);
            principals.Invalidate(user.Id);
        }
        return Outcome.FlashThenRedirect(new Toast("success", "Email verified. Welcome aboard!"), authenticated ? "/lots" : "/login");
    }

    // ---------- password reset ----------
    // The token is a time-limited data-protection payload: user id plus a fingerprint of the current password hash. Nothing is
    // stored; changing the password changes the fingerprint, so a link works once and dies with any later password change.

    static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(60);
    const string ResetPurpose = "password-reset";

    static string HashFingerprint(string passwordHash) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(passwordHash)))[..16];

    /// <summary>The 60-minute reset link; minted by the job when it runs.</summary>
    internal static string ResetUrl(IDataProtectionProvider dp, string baseUrl, User user) =>
        $"{baseUrl}/reset-password?token={Uri.EscapeDataString(dp.CreateProtector(ResetPurpose).ToTimeLimitedDataProtector().Protect($"{user.Id}:{HashFingerprint(user.PasswordHash)}", ResetLifetime))}";

    /// <summary>Always answers the same way (back, no flash: the page shows the sent state), whether or not the email is registered. Demo accounts share public inboxes, so
    /// they never get a link.</summary>
    public async Task<Outcome> RequestPasswordResetAsync(ForgotPasswordInput input, CancellationToken ct)
    {
        var email = Normalize(input.Email!);
        var user = await db.Users.Find(u => u.Email == email).Project(u => new { u.Id, u.IsDemo }).FirstOrDefaultAsync(ct);
        // A failed dispatch is logged, not surfaced: an error only for registered addresses would reveal who has an account.
        if (user is { IsDemo: false })
            try { await jobs.DispatchAsync(new SendPasswordResetEmail(user.Id), ct); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Could not queue a password reset for user {UserId}", user.Id); }
        return new Outcome();
    }

    /// <summary>Customers need 8 characters (sign-up), staff 12 (team creation).</summary>
    public static int MinPasswordLength(User user) => user.Role == UserRole.Customer ? 8 : 12;

    /// <summary>The account a reset token belongs to, or null when the token is malformed, expired, used or superseded.</summary>
    public async Task<User?> UserForResetTokenAsync(string? token, CancellationToken ct)
    {
        string payload;
        try { payload = dp.CreateProtector(ResetPurpose).ToTimeLimitedDataProtector().Unprotect(token ?? ""); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
        var parts = payload.Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var userId)) return null;
        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        return user is { IsDemo: false } && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(HashFingerprint(user.PasswordHash)), System.Text.Encoding.ASCII.GetBytes(parts[1])) ? user : null;
    }

    /// <summary>Sets the new password, rotates the session stamp (every other signed-in device is signed out) and marks the
    /// email verified, since only its owner could open the link. The update is conditional on the old hash, so two tabs
    /// using one link cannot both succeed.</summary>
    public async Task<Outcome> ResetPasswordAsync(ResetPasswordInput input, CancellationToken ct)
    {
        var user = await UserForResetTokenAsync(input.Token, ct);
        if (user is null)
            return Outcome.FlashThenRedirect(new Toast("error", "That reset link has expired or was already used. Ask for a new one."), "/forgot-password");
        // Staff accounts keep the 12-character floor that team creation enforces.
        if (input.Password!.Length < MinPasswordLength(user))
            return Outcome.Fail("password", $"Use at least {MinPasswordLength(user)} characters.");
        var oldHash = user.PasswordHash;
        var newHash = Hasher.HashPassword(user, input.Password!);
        var update = Builders<User>.Update.Set(u => u.PasswordHash, newHash).Set(u => u.SessionStamp, Guid.NewGuid().ToString("N"));
        if (user.EmailVerifiedAt is null) update = update.Set(u => u.EmailVerifiedAt, DateTime.UtcNow);
        var changed = await db.Users.UpdateOneAsync(u => u.Id == user.Id && u.PasswordHash == oldHash, update, cancellationToken: ct);
        if (changed.ModifiedCount == 0)
            return Outcome.FlashThenRedirect(new Toast("error", "That reset link has expired or was already used. Ask for a new one."), "/forgot-password");
        principals.Invalidate(user.Id);
        return Outcome.FlashThenRedirect(new Toast("success", "Password changed. Log in with your new password."), "/login");
    }
}
