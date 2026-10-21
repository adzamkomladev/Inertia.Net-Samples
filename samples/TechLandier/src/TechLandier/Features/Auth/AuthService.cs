using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TechLandier.Data;
using TechLandier.Infrastructure;
using static TechLandier.Features.AuthHelpers;

namespace TechLandier.Features;

/// Verification tokens and data-protection stay here; the Auth.cs handlers own the page composition
/// (Render/Back/flash/redirects) and turn these plain outcomes into Inertia responses.
public static partial class AuthService
{
    public sealed record LoginResult(SaveResult Result, User? User);
    public sealed record RegisterResult(SaveResult Result, User? User);

    internal static async Task<LoginResult> LoginAsync(
        LoginInput input,
        AppDb db,
        CancellationToken ct
    )
    {
        var email = Normalize(input.Email!);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        var verified = Hasher.VerifyHashedPassword(
            user ?? DummyUser,
            user?.PasswordHash ?? DummyHash,
            input.Password!
        );
        var result = user is null ? PasswordVerificationResult.Failed : verified;
        if (result == PasswordVerificationResult.Failed)
            return new LoginResult(
                new SaveResult(Errors: new Dictionary<string, string>
                {
                    ["email"] = "These credentials do not match our records.",
                }),
                null
            );
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user!.PasswordHash = Hasher.HashPassword(user, input.Password!);
            await db.SaveChangesAsync(ct);
        }

        return new LoginResult(
            new SaveResult(RedirectUrl: SafeReturnUrl(input.ReturnUrl) ?? "/products"),
            user
        );
    }

    internal static async Task<RegisterResult> RegisterAsync(
        RegisterInput input,
        string baseUrl,
        AppDb db,
        JobQueue jobs,
        IDataProtectionProvider dp,
        CancellationToken ct
    )
    {
        var email = Normalize(input.Email!);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return new RegisterResult(
                new SaveResult(Errors: new Dictionary<string, string>
                {
                    ["email"] = "This email is already registered.",
                }),
                null
            );
        var user = new User { Email = email, Name = input.Name!.Trim() };
        user.PasswordHash = Hasher.HashPassword(user, input.Password!);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e)
            when (e.InnerException
                    is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            ) // concurrent signup
        {
            return new RegisterResult(
                new SaveResult(Errors: new Dictionary<string, string>
                {
                    ["email"] = "This email is already registered.",
                }),
                null
            );
        }

        await jobs.EnqueueAsync(
            db,
            Emails.Verify(user.Email, user.Name, VerifyUrl(baseUrl, dp, user)),
            ct: ct
        );
        return new RegisterResult(new SaveResult(RedirectUrl: "/email/verify"), user);
    }

    internal static async Task<SaveResult> SendVerificationAsync(
        Guid userId,
        string baseUrl,
        AppDb db,
        JobQueue jobs,
        IDataProtectionProvider dp,
        CancellationToken ct
    )
    {
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null || user.EmailVerifiedAt is not null)
            return new SaveResult(RedirectUrl: "/products");
        await jobs.EnqueueAsync(
            db,
            Emails.Verify(user.Email, user.Name, VerifyUrl(baseUrl, dp, user)),
            ct: ct
        );
        return new SaveResult(FlashType: "success", FlashMessage: "A new verification link has been sent.");
    }

    internal static async Task<SaveResult> ConfirmEmailAsync(
        string? token,
        bool authenticated,
        AppDb db,
        IDataProtectionProvider dp,
        CancellationToken ct
    )
    {
        string payload;
        try
        {
            payload = Protector(dp).Unprotect(token ?? "");
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return new SaveResult(
                FlashType: "error",
                FlashMessage: "That verification link is invalid or has expired.",
                RedirectUrl: authenticated ? "/email/verify" : "/login"
            );
        }

        var parts = payload.Split(':', 2);
        var user =
            parts.Length == 2 && Guid.TryParse(parts[0], out var userId)
                ? await db.Users.FindAsync([userId], ct)
                : null;
        if (
            user is null
            || !string.Equals(user.Email, parts[^1], StringComparison.OrdinalIgnoreCase)
        )
        {
            return new SaveResult(
                FlashType: "error",
                FlashMessage: "That verification link is no longer valid.",
                RedirectUrl: "/login"
            );
        }

        user.EmailVerifiedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new SaveResult(
            FlashType: "success",
            FlashMessage: "Email verified. Welcome aboard!",
            RedirectUrl: authenticated ? "/products" : "/login"
        );
    }

    internal // The claim holds the asset key (stable); SharedAuth turns it into a URL (presigned URLs change every request).
    static async Task<string?> AvatarKeyAsync(AppDb db, User user, CancellationToken ct) =>
        user.AvatarAssetId is not { } assetId
            ? null
            : await db
                .Assets.Where(a => a.Id == assetId)
                .Select(a => a.Key)
                .FirstOrDefaultAsync(ct);
}
