using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Features;
using static TechLandier.Features.AuthHelpers;

namespace TechLandier.Infrastructure;

/// <summary>The cookie-facing half of authentication. Data access lives in <see cref="AuthService"/>.</summary>
public static class AuthHttp
{
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
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDb>();
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
            await AuthService.AvatarKeyAsync(db, user, context.HttpContext.RequestAborted)
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

    public static object SharedAuth(HttpContext ctx)
    {
        var u = ctx.User;
        if (u.UserIdOrNull() is not { } id)
            return new { user = (object?)null };
        return new
        {
            user = new
            {
                id,
                name = Claim(u, ClaimTypes.Name),
                email = Claim(u, ClaimTypes.Email),
                verified = Claim(u, VerifiedClaim) == "true",
                avatarUrl = Claim(u, AvatarClaim) is { } key
                    ? ctx.RequestServices.GetRequiredService<Storage>().Url(key)
                    : null,
                theme = Claim(u, ThemeClaim) ?? "system",
            },
        };
    }
}
