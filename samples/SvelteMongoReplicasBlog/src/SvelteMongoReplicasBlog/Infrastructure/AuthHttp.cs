using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Features;
using static SvelteMongoReplicasBlog.Features.AuthHelpers;

namespace SvelteMongoReplicasBlog.Infrastructure;

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
            await AuthService.AvatarKeyAsync(db, user, context.HttpContext.RequestAborted)
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

    public static object SharedAuth(HttpContext ctx)
    {
        var user = ctx.User;
        if (user.UserIdOrNull() is not { } id)
            return new { user = (object?)null };
        return new
        {
            user = new
            {
                id,
                name = Claim(user, ClaimTypes.Name),
                email = Claim(user, ClaimTypes.Email),
                verified = Claim(user, VerifiedClaim) == "true",
                avatarUrl = Claim(user, AvatarClaim) is { } key
                    ? ctx.RequestServices.GetRequiredService<Storage>().Url(key)
                    : null,
                theme = Claim(user, ThemeClaim) ?? "system",
            },
        };
    }
}
