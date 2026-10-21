using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using TechLandier.Data;
using TechLandier.Infrastructure;
using static TechLandier.Features.AuthHelpers;

namespace TechLandier.Features;

public static partial class AuthHelpers
{
    public const string VerifiedClaim = "email_verified";
    internal const string AvatarClaim = "avatar";
    internal const string ThemeClaim = "theme";
    internal static readonly PasswordHasher<User> Hasher = new();
    internal // Verified against when the email is unknown so response time doesn't reveal registered emails.
    static readonly User DummyUser = new() { Email = "", Name = "" };
    internal static readonly string DummyHash = Hasher.HashPassword(
        DummyUser,
        "dummy-password-for-timing"
    );

    internal static ClaimsPrincipal Principal(User user, string? avatarKey)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email),
            new(VerifiedClaim, user.EmailVerifiedAt is null ? "false" : "true"),
            new(ThemeClaim, user.Preferences.Theme),
        ];
        if (user.Role == "admin")
            claims.Add(new(ClaimTypes.Role, "admin"));
        if (avatarKey is not null)
            claims.Add(new(AvatarClaim, avatarKey));
        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)
        );
    }

    internal static string? Claim(ClaimsPrincipal p, string type) => p.FindFirstValue(type);

    internal static string Normalize(string email) => email.Trim().ToLowerInvariant();

    internal static ITimeLimitedDataProtector Protector(IDataProtectionProvider dp) =>
        dp.CreateProtector("email-verification").ToTimeLimitedDataProtector();

    internal static string VerifyUrl(
        HttpContext ctx,
        IConfiguration config,
        IDataProtectionProvider dp,
        User user
    )
    {
        var token = Protector(dp).Protect($"{user.Id}:{user.Email}", TimeSpan.FromHours(24));
        var baseUrl =
            config.AppBaseUrl()
            ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";
        return $"{baseUrl}/email/verify/confirm?token={Uri.EscapeDataString(token)}";
    }

    internal static string? SafeReturnUrl(string? url) =>
        url is { Length: > 0 }
        && Microsoft.AspNetCore.Http.HttpResults.RedirectHttpResult.IsLocalUrl(url)
            ? url
            : null;
}

public static partial class AuthService
{
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
