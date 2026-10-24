using Microsoft.AspNetCore.DataProtection;
using TechLandier.Data;

namespace TechLandier.Features;

public static class AuthUtils
{
    public static string? SafeReturnUrl(string? url) =>
        url is { Length: > 0 } && Microsoft.AspNetCore.Http.HttpResults.RedirectHttpResult.IsLocalUrl(url) ? url : null;

    /// The verification link is a time-limited protected "userId:email" token.
    public static string VerifyUrl(IDataProtectionProvider dp, string baseUrl, User user)
    {
        var token = Protector(dp).Protect($"{user.Id}:{user.Email}", TimeSpan.FromHours(24));
        return $"{baseUrl.TrimEnd('/')}/email/verify/confirm?token={Uri.EscapeDataString(token)}";
    }

    public static ITimeLimitedDataProtector Protector(IDataProtectionProvider dp) => dp.CreateProtector("email-verification").ToTimeLimitedDataProtector();

}
