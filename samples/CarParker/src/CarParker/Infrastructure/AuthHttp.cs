using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using CarParker.Features;
using static CarParker.Features.AuthHelpers;

namespace CarParker.Infrastructure;

/// <summary>The cookie-facing half of authentication.</summary>
public static class AuthHttp
{
    static readonly string[] TrackedClaims =
        [VerifiedClaim, ClaimTypes.Name, ClaimTypes.Email, RoleClaim, PhoneClaim, AvatarClaim, ThemeClaim];

    public static async Task SignInAsync(HttpContext ctx, ClaimsPrincipal principal, bool persistent) =>
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = persistent });

    /// Runs on every authenticated request: drops cookies for deleted users and keeps the claims (verified, name, avatar, theme) fresh.
    public static async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var id = context.Principal?.UserIdOrNull();
        var ct = context.HttpContext.RequestAborted;
        var account = id is null ? null : await context.HttpContext.RequestServices.GetRequiredService<AuthService>().FindAsync(id.Value, ct);
        if (account is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        var fresh = Principal(account.User, account.AvatarKey);
        if (TrackedClaims.Any(type => context.Principal!.FindFirstValue(type) != fresh.FindFirstValue(type)))
        {
            context.ReplacePrincipal(fresh);
            context.ShouldRenew = true;
        }
    }

    public static SharedAuthDto SharedAuth(HttpContext ctx)
    {
        var u = ctx.User;
        if (u.UserIdOrNull() is not { } id) return new SharedAuthDto(null);
        return new SharedAuthDto(new SharedAuthUser(
            id,
            u.FindFirstValue(ClaimTypes.Name),
            u.FindFirstValue(ClaimTypes.Email),
            u.FindFirstValue(VerifiedClaim) == "true",
            u.FindFirstValue(RoleClaim) ?? "customer",
            u.FindFirstValue(PhoneClaim),
            u.FindFirstValue(AvatarClaim) is { } key ? ctx.RequestServices.GetRequiredService<Storage>().Url(key) : null,
            u.FindFirstValue(ThemeClaim) ?? "system"));
    }
}

/// <summary>The signed-in user in the shared <c>auth</c> prop; typed for the generated TypeScript declarations.</summary>
public sealed record SharedAuthUser(
    Guid Id, string? Name, string? Email, bool Verified, string Role, string? Phone, string? AvatarUrl, string Theme);

public sealed record SharedAuthDto(SharedAuthUser? User);
