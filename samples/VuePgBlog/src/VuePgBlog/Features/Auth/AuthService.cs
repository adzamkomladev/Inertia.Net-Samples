using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VuePgBlog.Data;
using VuePgBlog.Infrastructure;
using static VuePgBlog.Features.AuthHelpers;

namespace VuePgBlog.Features;

/// Cookie sign-in/sign-out, verification tokens and data-protection stay here; the Auth.cs handlers own the
/// page composition (Render/Back/flash/redirects) and turn these plain outcomes into Inertia responses.
public static partial class AuthService
{
    internal static async Task<SaveResult> LoginAsync(
        LoginInput input,
        HttpContext ctx,
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
            return new SaveResult(Errors: new Dictionary<string, string>
            {
                ["email"] = "These credentials do not match our records.",
            });
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user!.PasswordHash = Hasher.HashPassword(user, input.Password!);
            await db.SaveChangesAsync(ct);
        }

        await SignInAsync(ctx, user!, await AvatarKeyAsync(db, user!, ct), input.Remember);
        return new SaveResult(RedirectUrl: SafeReturnUrl(input.ReturnUrl) ?? "/products");
    }

    internal static async Task<SaveResult> RegisterAsync(
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
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return new SaveResult(Errors: new Dictionary<string, string>
            {
                ["email"] = "This email is already registered.",
            });
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
            return new SaveResult(Errors: new Dictionary<string, string>
            {
                ["email"] = "This email is already registered.",
            });
        }

        await jobs.EnqueueAsync(
            db,
            Emails.Verify(user.Email, user.Name, VerifyUrl(ctx, config, dp, user)),
            ct: ct
        );
        await SignInAsync(ctx, user, null, persistent: false);
        return new SaveResult(RedirectUrl: "/email/verify");
    }

    internal static async Task<IResult> LogoutAsync(HttpContext ctx)
    {
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/login");
    }

    internal static async Task<SaveResult> SendVerificationAsync(
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        IDataProtectionProvider dp,
        IConfiguration config,
        CancellationToken ct
    )
    {
        var user = await db.Users.FindAsync([ctx.User.UserId()], ct);
        if (user is null || user.EmailVerifiedAt is not null)
            return new SaveResult(RedirectUrl: "/products");
        await jobs.EnqueueAsync(
            db,
            Emails.Verify(user.Email, user.Name, VerifyUrl(ctx, config, dp, user)),
            ct: ct
        );
        return new SaveResult(FlashType: "success", FlashMessage: "A new verification link has been sent.");
    }

    internal static async Task<SaveResult> ConfirmEmailAsync(
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
            return new SaveResult(
                FlashType: "error",
                FlashMessage: "That verification link is invalid or has expired.",
                RedirectUrl: ctx.User.Identity?.IsAuthenticated == true ? "/email/verify" : "/login");
        }

        var parts = payload.Split(':', 2);
        var user = await db.Users.FindAsync([Guid.Parse(parts[0])], ct);
        if (
            user is null
            || !string.Equals(user.Email, parts[1], StringComparison.OrdinalIgnoreCase)
        )
        {
            return new SaveResult(
                FlashType: "error",
                FlashMessage: "That verification link is no longer valid.",
                RedirectUrl: "/login");
        }

        user.EmailVerifiedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new SaveResult(
            FlashType: "success",
            FlashMessage: "Email verified. Welcome aboard!",
            RedirectUrl: ctx.User.Identity?.IsAuthenticated == true ? "/products" : "/login");
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
            : await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
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
            || current.IsInRole("admin") != (user.Role == "admin")
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
                .Assets.Where(a => a.Id == assetId)
                .Select(a => a.Key)
                .FirstOrDefaultAsync(ct);
}
