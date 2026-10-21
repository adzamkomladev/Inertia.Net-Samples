using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;

namespace VuePgBlog.Features;

/// Human-facing identifiers: TL-2026-00001 orders, T-0001 tickets, receipts, tracking tokens.
public static class OrderNumbers
{
    const string LockName = "techlandier-numbering";

    /// <summary>
    /// Advisory lock on 'techlandier-numbering' so concurrent issuers can't collide.
    /// Call inside the owning transaction (the lock is xact-scoped).
    /// </summary>
    static async Task LockAsync(AppDb db, CancellationToken ct) =>
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({LockName}))", ct);

    /// Next order number TL-2026-00001, computed under the numbering lock.
    public static async Task<string> NextOrderAsync(AppDb db, CancellationToken ct = default)
    {
        await LockAsync(db, ct);
        var prefix = $"TL-{DateTime.UtcNow.Year}-";
        var last = await db.Orders.IgnoreQueryFilters().Where(o => o.Number.StartsWith(prefix))
            .OrderByDescending(o => o.Number).Select(o => o.Number).FirstOrDefaultAsync(ct);
        var next = last is null ? 1 : int.Parse(last[prefix.Length..]) + 1;
        return $"{prefix}{next:D5}";
    }

    /// Next ticket number T-0001, computed under the numbering lock.
    public static async Task<string> NextTicketAsync(AppDb db, CancellationToken ct = default)
    {
        await LockAsync(db, ct);
        const string prefix = "T-";
        var last = await db.Tickets.IgnoreQueryFilters().Where(t => t.Number.StartsWith(prefix))
            .OrderByDescending(t => t.Number).Select(t => t.Number).FirstOrDefaultAsync(ct);
        var next = last is null ? 1 : int.Parse(last[prefix.Length..]) + 1;
        return $"{prefix}{next:D4}";
    }

    /// Telecom-style receipt: R-20260101-3f9c2a.
    public static string Receipt(DateTime utcNow) => $"R-{utcNow:yyyyMMdd}-{Token(6)}";

    /// Unguessable tracking token for guest order pages; fits AccessToken(24).
    public static string Access() => Token(22);

    static string Token(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
}
