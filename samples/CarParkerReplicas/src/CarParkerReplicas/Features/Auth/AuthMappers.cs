using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;

namespace CarParker.Features;

public static class AuthHelpers
{
    public const string VerifiedClaim = "email_verified";
    public const string RoleClaim = "role";
    public const string AvatarClaim = "avatar";
    public const string ThemeClaim = "theme";
    public const string PhoneClaim = "phone";

    public static ClaimsPrincipal Principal(User user, string? avatarKey)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email),
            new(VerifiedClaim, user.EmailVerifiedAt is null ? "false" : "true"),
            new(RoleClaim, user.Role.ToString().ToLowerInvariant()),
            new(ThemeClaim, user.Preferences.Theme),
        ];
        if (user.Phone is { Length: > 0 } phone) claims.Add(new(PhoneClaim, phone));
        if (avatarKey is not null) claims.Add(new(AvatarClaim, avatarKey));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
