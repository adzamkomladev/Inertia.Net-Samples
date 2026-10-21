using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Infrastructure;
using static SvelteMongoReplicasBlog.Features.AuthHelpers;

namespace SvelteMongoReplicasBlog.Features;

public static partial class AuthService
{
    public sealed record RegisterResult(Outcome Outcome, User? User);

    internal // The claim holds the asset key (stable); SharedAuth turns it into a URL (presigned URLs change every request).
    static async Task<string?> AvatarKeyAsync(AppDb db, User user, CancellationToken ct) =>
        user.AvatarAssetId is not { } assetId
            ? null
            : await db
                .Assets.Find(a => a.Id == assetId)
                .Project(a => a.Key)
                .FirstOrDefaultAsync(ct);

    internal static async Task<User?> LoginAsync(
        LoginInput input,
        AppDb db,
        CancellationToken ct
    )
    {
        var email = Normalize(input.Email!);
        var user = await db.Users.Find(u => u.Email == email).FirstOrDefaultAsync(ct);
        var verified = Hasher.VerifyHashedPassword(
            user ?? DummyUser,
            user?.PasswordHash ?? DummyHash,
            input.Password!
        );
        var result = user is null ? PasswordVerificationResult.Failed : verified;
        if (result == PasswordVerificationResult.Failed)
            return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user!.PasswordHash = Hasher.HashPassword(user, input.Password!);
            await db.Users.UpdateOneAsync(
                u => u.Id == user.Id,
                Builders<User>.Update.Set(u => u.PasswordHash, user.PasswordHash),
                cancellationToken: ct
            );
        }

        return user;
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
        if (await db.Users.Find(u => u.Email == email).AnyAsync(ct))
            return new RegisterResult(Outcome.Fail("email", "This email is already registered."), null);
        var user = new User { Email = email, Name = input.Name!.Trim() };
        user.PasswordHash = Hasher.HashPassword(user, input.Password!);
        try
        {
            await db.TransactionAsync(
                async session =>
                {
                    await db.Users.InsertOneAsync(session, user, cancellationToken: ct);
                    await jobs.EnqueueAsync(
                        db,
                        Emails.Verify(user.Email, user.Name, VerifyUrl(baseUrl, dp, user)),
                        session: session,
                        ct: ct
                    );
                },
                ct
            );
            jobs.Wake();
        }
        catch (MongoWriteException e)
            when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return new RegisterResult(
                Outcome.Fail("email", "This email is already registered."),
                null
            );
        }

        return new RegisterResult(Outcome.RedirectTo("/email/verify"), user);
    }

    internal static async Task<Outcome> SendVerificationAsync(
        Guid userId,
        string baseUrl,
        AppDb db,
        JobQueue jobs,
        IDataProtectionProvider dp,
        CancellationToken ct
    )
    {
        var user = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        if (user is null || user.EmailVerifiedAt is not null)
            return Outcome.RedirectTo("/posts");
        await jobs.EnqueueAsync(
            db,
            Emails.Verify(user.Email, user.Name, VerifyUrl(baseUrl, dp, user)),
            ct: ct
        );
        return Outcome.FlashBack(new Toast("success", "A new verification link has been sent."));
    }

    internal static async Task<Outcome> ConfirmEmailAsync(
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
            return Outcome.FlashThenRedirect(
                new Toast("error", "That verification link is invalid or has expired."),
                authenticated ? "/email/verify" : "/login"
            );
        }

        var parts = payload.Split(':', 2);
        var user =
            parts.Length == 2 && Guid.TryParse(parts[0], out var userId)
                ? await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct)
                : null;
        if (
            user is null
            || !string.Equals(user.Email, parts[^1], StringComparison.OrdinalIgnoreCase)
        )
        {
            return Outcome.FlashThenRedirect(
                new Toast("error", "That verification link is no longer valid."),
                "/login"
            );
        }

        if (user.EmailVerifiedAt is null)
            await db.Users.UpdateOneAsync(
                u => u.Id == user.Id && u.Email == user.Email && u.EmailVerifiedAt == null,
                Builders<User>.Update.Set(u => u.EmailVerifiedAt, DateTime.UtcNow),
                cancellationToken: ct
            );
        return Outcome.FlashThenRedirect(
            new Toast("success", "Email verified. Welcome aboard!"),
            authenticated ? "/posts" : "/login"
        );
    }
}
