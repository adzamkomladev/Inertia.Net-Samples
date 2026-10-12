using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Infrastructure;
using static SvelteMongoBlog.Features.AuthHelpers;

namespace SvelteMongoBlog.Features;

public static partial class AuthService
{
    internal static async Task<IResult> LoginAsync(
        LoginInput input,
        HttpContext ctx,
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
            return Back()
                .WithErrors(
                    new Dictionary<string, string>
                    {
                        ["email"] = "These credentials do not match our records.",
                    }
                );
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user!.PasswordHash = Hasher.HashPassword(user, input.Password!);
            await db.Users.UpdateOneAsync(
                u => u.Id == user.Id,
                Builders<User>.Update.Set(u => u.PasswordHash, user.PasswordHash),
                cancellationToken: ct
            );
        }

        await SignInAsync(ctx, user!, await AvatarKeyAsync(db, user!, ct), input.Remember);
        return Results.Redirect(SafeReturnUrl(input.ReturnUrl) ?? "/posts");
    }

    internal static async Task<IResult> RegisterAsync(
        RegisterInput input,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        IDataProtectionProvider dp,
        IConfiguration config,
        CancellationToken ct
    )
    {
        var email = Normalize(input.Email!);
        if (await db.Users.Find(u => u.Email == email).AnyAsync(ct))
            return Back()
                .WithErrors(
                    new Dictionary<string, string>
                    {
                        ["email"] = "This email is already registered.",
                    }
                );
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
                        Emails.Verify(user.Email, user.Name, VerifyUrl(ctx, config, dp, user)),
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
            return Back()
                .WithErrors(
                    new Dictionary<string, string>
                    {
                        ["email"] = "This email is already registered.",
                    }
                );
        }

        await SignInAsync(ctx, user, null, persistent: false);
        return Results.Redirect("/email/verify");
    }

    internal static async Task<IResult> LogoutAsync(HttpContext ctx)
    {
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/login");
    }

    internal static async Task<IResult> SendVerificationAsync(
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        IDataProtectionProvider dp,
        IConfiguration config,
        CancellationToken ct
    )
    {
        var user = await db.Users.Find(u => u.Id == ctx.User.UserId()).FirstOrDefaultAsync(ct);
        if (user is null || user.EmailVerifiedAt is not null)
            return Results.Redirect("/posts");
        await jobs.EnqueueAsync(
            db,
            Emails.Verify(user.Email, user.Name, VerifyUrl(ctx, config, dp, user)),
            ct: ct
        );
        return Back()
            .WithFlash("toast", new Toast("success", "A new verification link has been sent."));
    }

    internal static async Task<IResult> ConfirmEmailAsync(
        string? token,
        HttpContext ctx,
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
            ctx.Inertia()
                .Flash(
                    "toast",
                    new Toast("error", "That verification link is invalid or has expired.")
                );
            return Results.Redirect(
                ctx.User.Identity?.IsAuthenticated == true ? "/email/verify" : "/login"
            );
        }

        var parts = payload.Split(':', 2);
        var user = await db.Users.Find(u => u.Id == Guid.Parse(parts[0])).FirstOrDefaultAsync(ct);
        if (
            user is null
            || !string.Equals(user.Email, parts[1], StringComparison.OrdinalIgnoreCase)
        )
        {
            ctx.Inertia()
                .Flash("toast", new Toast("error", "That verification link is no longer valid."));
            return Results.Redirect("/login");
        }

        if (user.EmailVerifiedAt is null)
            await db.Users.UpdateOneAsync(
                u => u.Id == user.Id && u.Email == user.Email && u.EmailVerifiedAt == null,
                Builders<User>.Update.Set(u => u.EmailVerifiedAt, DateTime.UtcNow),
                cancellationToken: ct
            );
        ctx.Inertia().Flash("toast", new Toast("success", "Email verified. Welcome aboard!"));
        return Results.Redirect(ctx.User.Identity?.IsAuthenticated == true ? "/posts" : "/login");
    }

    public static async Task SignInAsync(
        HttpContext ctx,
        User user,
        string? avatarKey,
        bool persistent
    ) =>
        await ctx.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            Principal(user, avatarKey),
            new AuthenticationProperties { IsPersistent = persistent }
        );

    /// Runs on every authenticated request: drops cookies for deleted users and keeps claims (verified, name, avatar, theme) fresh.
    public static async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var id = context.Principal?.UserIdOrNull();
        var services = context.HttpContext.RequestServices;
        var db = services.GetRequiredService<AppDb>();
        var user = id is null
            ? null
            : await db
                .Users.Find(u => u.Id == id)
                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);
        if (user is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme
            );
            return;
        }

        var fresh = Principal(
            user,
            await AvatarKeyAsync(db, user, context.HttpContext.RequestAborted)
        );
        var current = context.Principal!;
        if (
            Claim(current, VerifiedClaim) != Claim(fresh, VerifiedClaim)
            || Claim(current, ClaimTypes.Name) != Claim(fresh, ClaimTypes.Name)
            || Claim(current, ClaimTypes.Email) != Claim(fresh, ClaimTypes.Email)
            || Claim(current, AvatarClaim) != Claim(fresh, AvatarClaim)
            || Claim(current, ThemeClaim) != Claim(fresh, ThemeClaim)
        )
        {
            context.ReplacePrincipal(fresh);
            context.ShouldRenew = true;
        }
    }

    internal // The claim holds the asset key (stable); SharedAuth turns it into a URL (presigned URLs change every request).
    static async Task<string?> AvatarKeyAsync(AppDb db, User user, CancellationToken ct) =>
        user.AvatarAssetId is not { } assetId
            ? null
            : await db
                .Assets.Find(a => a.Id == assetId)
                .Project(a => a.Key)
                .FirstOrDefaultAsync(ct);
}
