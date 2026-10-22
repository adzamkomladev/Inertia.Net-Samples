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

    public static Task SignInAsync(HttpContext ctx, ClaimsPrincipal principal, bool persistent) =>
        ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = persistent });

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

    public static object SharedAuth(HttpContext ctx)
    {
        var u = ctx.User;
        if (u.UserIdOrNull() is not { } id) return new { user = (object?)null };
        return new
        {
            user = new
            {
                id,
                name = u.FindFirstValue(ClaimTypes.Name),
                email = u.FindFirstValue(ClaimTypes.Email),
                verified = u.FindFirstValue(VerifiedClaim) == "true",
                role = u.FindFirstValue(RoleClaim) ?? "customer",
                phone = u.FindFirstValue(PhoneClaim),
                avatarUrl = u.FindFirstValue(AvatarClaim) is { } key ? ctx.RequestServices.GetRequiredService<Storage>().Url(key) : null,
                theme = u.FindFirstValue(ThemeClaim) ?? "system",
            },
        };
    }
}
