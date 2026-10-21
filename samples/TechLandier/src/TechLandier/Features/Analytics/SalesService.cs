using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// Sales analytics: charts over SalesDailyStat rollups plus live top sellers from order lines.
/// Pure data loaders; the Admin.cs handlers own the Defer/Cache composition (cache keys live there too).
public static partial class SalesService
{
    internal static async Task<object> KpisAsync(AppDb db, DateOnly since, string[] paid, CancellationToken ct)
    {
        var sinceUtc = since.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return new
        {
            Revenue = await db.SalesDailyStats.AsNoTracking().Where(s => s.Date >= since).SumAsync(s => (decimal?)s.Revenue, ct) ?? 0m,
            Orders = await db.SalesDailyStats.AsNoTracking().Where(s => s.Date >= since).SumAsync(s => s.OrderCount, ct),
            BnplShare = await db.SalesDailyStats.AsNoTracking().Where(s => s.Date >= since).SumAsync(s => (decimal?)s.BnplShare, ct) ?? 0m,
            Pickups = await db.SalesDailyStats.AsNoTracking().Where(s => s.Date >= since).SumAsync(s => s.PickupCount, ct),
            Deliveries = await db.SalesDailyStats.AsNoTracking().Where(s => s.Date >= since).SumAsync(s => s.DeliveryCount, ct),
            ProductViews = await db.ProductViews.AsNoTracking().CountAsync(v => v.At >= sinceUtc, ct),
        };
    }

    internal static async Task<object> SeriesAsync(AppDb db, DateOnly since, CancellationToken ct)
    {
        var rows = await db.SalesDailyStats.AsNoTracking()
            .Where(s => s.Date >= since)
            .OrderBy(s => s.Date)
            .Select(s => new { s.Date, s.Revenue, s.OrderCount, s.BnplShare, s.PickupCount, s.DeliveryCount })
            .ToListAsync(ct);
        return rows;
    }

    internal static async Task<object> TopProductsAsync(AppDb db, DateTime sinceUtc, string[] paid, CancellationToken ct) =>
        await db.OrderItems.AsNoTracking()
            .Where(i => i.Order.CreatedAt >= sinceUtc && paid.Contains(i.Order.Status))
            .GroupBy(i => new { i.ProductId, i.Name })
            .OrderByDescending(g => g.Sum(i => i.LineTotal))
            .Take(8)
            .Select(g => new { Name = g.Key.Name, Revenue = g.Sum(i => i.LineTotal), Qty = g.Sum(i => i.Qty) })
            .ToListAsync(ct);

    internal static async Task<object> CategoryShareAsync(AppDb db, DateTime sinceUtc, string[] paid, CancellationToken ct) =>
        await db.OrderItems.AsNoTracking()
            .Where(i => i.Order.CreatedAt >= sinceUtc && paid.Contains(i.Order.Status))
            .GroupBy(i => i.Product != null ? i.Product.Category.Name : "Other")
            .OrderByDescending(g => g.Sum(i => i.LineTotal))
            .Take(6)
            .Select(g => new { Label = g.Key, Revenue = g.Sum(i => i.LineTotal) })
            .ToListAsync(ct);
}
