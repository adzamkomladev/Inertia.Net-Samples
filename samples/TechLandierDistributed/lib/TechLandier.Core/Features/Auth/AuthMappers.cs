using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using TechLandier.Data;

namespace TechLandier.Features;

public static class AuthMappers
{
    public const string VerifiedClaim = "email_verified";
    public const string AvatarClaim = "avatar";
    public const string ThemeClaim = "theme";

    public static ClaimsPrincipal Principal(User user, string? avatarKey)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email),
            new(VerifiedClaim, user.EmailVerifiedAt is null ? "false" : "true"),
            new(ThemeClaim, user.Preferences.Theme),
        ];
        if (user.Role == "admin") claims.Add(new(ClaimTypes.Role, "admin"));
        if (avatarKey is not null) claims.Add(new(AvatarClaim, avatarKey));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

}
