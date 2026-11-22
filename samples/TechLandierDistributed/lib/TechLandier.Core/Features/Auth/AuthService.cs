using static TechLandier.Features.AuthUtils;
using System.Collections.Concurrent;
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

/// Accounts: sign-in checks, registration and email verification. The cookie itself is written by the handlers (AuthHttp).
public sealed class AuthService(AppDb db, IJobOutbox jobs, IDataProtectionProvider dp)
{
    static readonly PasswordHasher<User> Hasher = new();
    // Verified against when the email is unknown, so response time does not reveal registered emails.
    static readonly User DummyUser = new() { Email = "", Name = "" };
    static readonly string DummyHash = Hasher.HashPassword(DummyUser, "dummy-password-for-timing");

    /// A user with the asset key of their avatar, in one query. The claim holds the key (stable); SharedAuth turns it into a URL.
    public async Task<(User User, string? AvatarKey)?> FindAsync(Guid id, CancellationToken ct)
    {
        var row = await db.Users.AsNoTracking().Where(u => u.Id == id)
            .Select(u => new { User = u, AvatarKey = db.Assets.Where(a => a.Id == u.AvatarAssetId).Select(a => a.Key).FirstOrDefault() })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : (row.User, row.AvatarKey);
    }

    // The per-address rate limit is sized for a whole room behind one NAT (playbook), so the account itself stays the
    // abuse boundary for anonymous sign-ins: this in-process window throttles attempts per submitted address. It is
    // process-local by design (CarParkerReplicas: three replicas each hold their own window; the per-address limiter
    // still bounds the room, so the combined effect stays bounded).
    static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> Attempts = new();
    const int MaxAttemptsPerAccount = 30;
    static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(5);

    public async Task<AuthResult> LoginAsync(LoginInput input, CancellationToken ct)
    {
        var email = input.Email!.Trim().ToLowerInvariant();
        if (!AllowAttempt(email)) return new(SaveResult.Invalid("email", "Too many sign-in attempts for this account. Try again in a few minutes."));
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
        return new(new SaveResult(RedirectUrl: SafeReturnUrl(input.ReturnUrl) ?? (user.Role == "admin" ? "/admin" : "/products")), user, avatarKey);
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

    public async Task<AuthResult> RegisterAsync(RegisterInput input, CancellationToken ct)
    {
        var emailTaken = new AuthResult(SaveResult.Invalid("email", "This email is already registered."));
        var email = input.Email!.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return emailTaken;
        var user = new User { Email = email, Name = input.Name!.Trim() };
        user.PasswordHash = Hasher.HashPassword(user, input.Password!);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return emailTaken; // a concurrent signup won
        }
        // The user and the verification job commit together; the link is minted when the job runs.
        await jobs.StageAsync(db, new SendVerificationEmail(user.Id), ct);
        await tx.CommitAsync(ct);
        return new(new SaveResult(RedirectUrl: "/email/verify"), user);
    }

    public async Task<SaveResult> SendVerificationAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.EmailVerifiedAt is not null) return new(RedirectUrl: "/products");
        // Nothing is written, so the job is dispatched directly; a pending one for this user is reused.
        await jobs.DispatchAsync(new SendVerificationEmail(user.Id), ct);
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
