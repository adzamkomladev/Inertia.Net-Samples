using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Auth;
using VuePgReplicasAdvBlog.Features.Uploads;
using VuePgReplicasAdvBlog.Infrastructure;
using static VuePgReplicasAdvBlog.Features.Auth.AuthHelpers;

namespace VuePgReplicasAdvBlog.MainApp.Infrastructure;

public static class AuthHttp
{
    public static string? SafeReturnUrl(string? url) =>
        url is { Length: > 0 } && Microsoft.AspNetCore.Http.HttpResults.RedirectHttpResult.IsLocalUrl(url) ? url : null;

    public static string VerificationUrl(HttpContext ctx, IConfiguration config, IDataProtectionProvider protection, User user)
    {
        var baseUrl = config.RequiredAppBaseUrl();
        return $"{baseUrl}/email/verify/confirm?token={Uri.EscapeDataString(AuthService.VerificationToken(protection, user))}";
    }

    public static async Task SignInAsync(HttpContext ctx, User user, AppDb db, bool persistent, CancellationToken ct)
    {
        var principal = await AuthService.GetPrincipalAsync(user.Id, CookieAuthenticationDefaults.AuthenticationScheme, db, ct)
            ?? throw new InvalidOperationException("The user no longer exists.");
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = persistent });
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
                avatarUrl = user.FindFirstValue(AvatarClaim) is { } key ? ctx.RequestServices.GetRequiredService<Storage>().Url(key) : null,
                theme = user.FindFirstValue(ThemeClaim) ?? "system"
            }
        };
    }
}
