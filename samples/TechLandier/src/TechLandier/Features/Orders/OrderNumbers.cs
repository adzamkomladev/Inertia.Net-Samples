using System.Security.Cryptography;

namespace TechLandier.Features;

/// Human-facing identifiers: TL-2026-00001 orders, T-0001 tickets, receipts, tracking tokens.
/// The database-backed numbering sequences live in OrderService; the formats here are pure.
public static class OrderNumbers
{
    /// Telecom-style receipt: R-20260101-3f9c2a.
    public static string Receipt(DateTime utcNow) => $"R-{utcNow:yyyyMMdd}-{Token(6)}";

    /// Unguessable tracking token for guest order pages; fits AccessToken(24).
    public static string Access() => Token(22);

    static string Token(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
}
