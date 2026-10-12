using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using VuePgReplicasAdvBlog.Data;

namespace VuePgReplicasAdvBlog.Features.Auth;

public static class AuthHelpers
{
    public const string VerifiedClaim = "email_verified";
    public const string AvatarClaim = "avatar";
    public const string ThemeClaim = "theme";
    internal static readonly PasswordHasher<User> Hasher = new();
    // Unknown emails still incur password hashing to avoid disclosing registered addresses through timing.
    internal static readonly User DummyUser = new() { Email = "", Name = "" };
    internal static readonly string DummyHash = Hasher.HashPassword(DummyUser, "dummy-password-for-timing");

    public static ClaimsPrincipal Principal(User user, string? avatarKey, string authenticationType)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email),
            new(VerifiedClaim, user.EmailVerifiedAt is null ? "false" : "true"),
            new(ThemeClaim, user.Preferences.Theme)
        ];
        if (avatarKey is not null) claims.Add(new(AvatarClaim, avatarKey));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }

    public static bool NeedsRefresh(ClaimsPrincipal current, ClaimsPrincipal fresh) =>
        new[] { VerifiedClaim, ClaimTypes.Name, ClaimTypes.Email, AvatarClaim, ThemeClaim }
            .Any(type => current.FindFirstValue(type) != fresh.FindFirstValue(type));

    internal static string Normalize(string email) => email.Trim().ToLowerInvariant();
    internal static ITimeLimitedDataProtector Protector(IDataProtectionProvider protection) =>
        protection.CreateProtector("email-verification").ToTimeLimitedDataProtector();
}
