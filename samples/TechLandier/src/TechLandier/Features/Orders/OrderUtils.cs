using System.Security.Cryptography;

namespace TechLandier.Features;

public static class OrderUtils
{
    public static string NewReceiptNumber(DateTime at) => $"R-{at:yyyyMMdd}-{Token(6)}";

    /// Unguessable tracking token for guest order pages; fits Order.AccessToken (24).
    public static string NewAccessToken() => Token(11);

    static string Token(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

}
