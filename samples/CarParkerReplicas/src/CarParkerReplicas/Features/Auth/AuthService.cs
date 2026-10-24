using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;

namespace CarParker.Features;

public sealed class AuthService(AppDb db, JobQueue jobs, IDataProtectionProvider dp)
{
    static readonly PasswordHasher<User> Hasher = new();
    // Verified against when the email is unknown so response time doesn't reveal registered emails.
    static readonly User DummyUser = new() { Email = "", Name = "" };
    static readonly string DummyHash = Hasher.HashPassword(DummyUser, "dummy-password-for-timing");

    static string Normalize(string email) => email.Trim().ToLowerInvariant();

    ITimeLimitedDataProtector Protector => dp.CreateProtector("email-verification").ToTimeLimitedDataProtector();

    string VerifyUrl(string baseUrl, User user) =>
        $"{baseUrl}/email/verify/confirm?token={Uri.EscapeDataString(Protector.Protect($"{user.Id}:{user.Email}", TimeSpan.FromHours(24)))}";

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

    public async Task<AuthAccount?> LoginAsync(LoginInput input, CancellationToken ct)
    {
        var email = Normalize(input.Email!);
        var user = await db.Users.Find(u => u.Email == email).FirstOrDefaultAsync(ct);
        var result = Hasher.VerifyHashedPassword(user ?? DummyUser, user?.PasswordHash ?? DummyHash, input.Password!);
        if (user is null || result == PasswordVerificationResult.Failed) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            await db.Users.UpdateOneAsync(u => u.Id == user.Id,
                Builders<User>.Update.Set(u => u.PasswordHash, Hasher.HashPassword(user, input.Password!)), cancellationToken: ct);
        return await AccountAsync(user, ct);
    }

    public async Task<RegisterResult> RegisterAsync(RegisterInput input, string baseUrl, CancellationToken ct)
    {
        var user = new User { Email = Normalize(input.Email!), Name = input.Name!.Trim(), Role = UserRole.Customer };
        user.PasswordHash = Hasher.HashPassword(user, input.Password!);
        try
        {
            await db.TransactionAsync(async session =>
            {
                await db.Users.InsertOneAsync(session, user, cancellationToken: ct);
                await jobs.EnqueueAsync(db, Emails.Verify(user.Email, user.Name, VerifyUrl(baseUrl, user)), session, ct: ct);
            }, ct);
        }
        catch (MongoException e) when (e is MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey } or MongoCommandException { Code: 11000 })
        {
            return new RegisterResult(Outcome.Fail("email", "This email is already registered."), null);
        }
        jobs.Wake();
        return new RegisterResult(Outcome.RedirectTo("/email/verify"), new AuthAccount(user, null));
    }

    public async Task<Outcome> SendVerificationAsync(Guid userId, string baseUrl, CancellationToken ct)
    {
        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is null || user.EmailVerifiedAt is not null) return Outcome.RedirectTo("/lots");
        await jobs.EnqueueAsync(db, Emails.Verify(user.Email, user.Name, VerifyUrl(baseUrl, user)), ct: ct);
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
            await db.Users.UpdateOneAsync(u => u.Id == user.Id && u.Email == user.Email && u.EmailVerifiedAt == null,
                Builders<User>.Update.Set(u => u.EmailVerifiedAt, DateTime.UtcNow), cancellationToken: ct);
        return Outcome.FlashThenRedirect(new Toast("success", "Email verified. Welcome aboard!"), authenticated ? "/lots" : "/login");
    }
}
