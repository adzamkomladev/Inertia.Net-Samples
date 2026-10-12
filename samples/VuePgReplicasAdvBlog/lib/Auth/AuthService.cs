using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Email;
using VuePgReplicasAdvBlog.Infrastructure;
using static VuePgReplicasAdvBlog.Features.Auth.AuthHelpers;

namespace VuePgReplicasAdvBlog.Features.Auth;

public enum EmailConfirmation { Verified, InvalidToken, UnknownRecipient }

public static class AuthService
{
    public static async Task<User> LoginAsync(string emailAddress, string password, AppDb db, CancellationToken ct)
    {
        var email = Normalize(emailAddress);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        var verified = Hasher.VerifyHashedPassword(user ?? DummyUser, user?.PasswordHash ?? DummyHash, password);
        if (user is null || verified == PasswordVerificationResult.Failed)
            throw FeatureException.Invalid("email", "These credentials do not match our records.");
        if (verified == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, password);
            await db.SaveChangesAsync(ct);
        }
        return user;
    }

    public static async Task<User> RegisterAsync(string name, string emailAddress, string password, AppDb db, CancellationToken ct)
    {
        var email = Normalize(emailAddress);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw FeatureException.Invalid("email", "This email is already registered.");
        var user = new User { Email = email, Name = name.Trim() };
        user.PasswordHash = Hasher.HashPassword(user, password);
        db.Users.Add(user);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw FeatureException.Invalid("email", "This email is already registered.");
        }
        return user;
    }

    public static string VerificationToken(IDataProtectionProvider protection, User user) =>
        Protector(protection).Protect($"{user.Id}:{user.Email}", TimeSpan.FromHours(24));

    public static async Task SendVerificationAsync(User user, string verificationUrl, AppDb db, JobQueue jobs, CancellationToken ct) =>
        await jobs.EnqueueAsync(db, Emails.Verify(user.Email, user.Name, verificationUrl), ct: ct);

    public static async Task<EmailConfirmation> ConfirmEmailAsync(string? token, AppDb db, IDataProtectionProvider protection, CancellationToken ct)
    {
        string payload;
        try { payload = Protector(protection).Unprotect(token ?? ""); }
        catch (CryptographicException) { return EmailConfirmation.InvalidToken; }
        var parts = payload.Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var userId)) return EmailConfirmation.InvalidToken;
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null || !string.Equals(user.Email, parts[1], StringComparison.OrdinalIgnoreCase))
            return EmailConfirmation.UnknownRecipient;
        user.EmailVerifiedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return EmailConfirmation.Verified;
    }

    public static async Task<ClaimsPrincipal?> GetPrincipalAsync(Guid? id, string authenticationType, AppDb db, CancellationToken ct)
    {
        if (id is null) return null;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return null;
        var avatarKey = user.AvatarAssetId is { } assetId
            ? await db.Assets.Where(a => a.Id == assetId).Select(a => a.Key).FirstOrDefaultAsync(ct)
            : null;
        return Principal(user, avatarKey, authenticationType);
    }
}
