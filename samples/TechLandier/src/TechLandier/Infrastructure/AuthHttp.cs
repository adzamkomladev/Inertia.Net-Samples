using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TechLandier.Data;
using TechLandier.Features;
using static TechLandier.Features.AuthMappers;

namespace TechLandier.Infrastructure;

/// The cookie-facing half of authentication; account logic lives in <see cref="AuthService"/>.
public static class AuthHttp
{
    public static async Task SignInAsync(HttpContext ctx, User user, string? avatarKey, bool persistent) =>
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Principal(user, avatarKey),
            new AuthenticationProperties { IsPersistent = persistent });

    /// Runs on every authenticated request: drops cookies of deleted users and keeps the claims (verified, name, avatar, theme) fresh.
    public static async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var http = context.HttpContext;
        var found = context.Principal?.UserIdOrNull() is { } id
            ? await http.RequestServices.GetRequiredService<AuthService>().FindAsync(id, http.RequestAborted)
            : null;
        if (found is not { } account)
        {
            context.RejectPrincipal();
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        var fresh = Principal(account.User, account.AvatarKey);
        var current = context.Principal!;
        string?[] Snapshot(ClaimsPrincipal p) =>
            [.. new[] { VerifiedClaim, ClaimTypes.Name, ClaimTypes.Email, AvatarClaim, ThemeClaim }.Select(p.FindFirstValue), p.IsInRole("admin").ToString()];
        if (!Snapshot(current).SequenceEqual(Snapshot(fresh)))
        {
            context.ReplacePrincipal(fresh);
            context.ShouldRenew = true;
        }
    }

    public static object SharedAuth(HttpContext ctx)
    {
        var user = ctx.User;
        if (user.UserIdOrNull() is not { } id) return new { user = (object?)null };
        return new
        {
            user = new
            {
                id,
                name = user.FindFirstValue(ClaimTypes.Name),
                email = user.FindFirstValue(ClaimTypes.Email),
                verified = user.FindFirstValue(VerifiedClaim) == "true",
                role = user.FindFirstValue(ClaimTypes.Role) ?? "customer",
                avatarUrl = user.FindFirstValue(AvatarClaim) is { } key ? ctx.RequestServices.GetRequiredService<Storage>().Url(key) : null,
                theme = user.FindFirstValue(ThemeClaim) ?? "system",
            },
        };
    }
}
