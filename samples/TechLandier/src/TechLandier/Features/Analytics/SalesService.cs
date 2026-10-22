using Microsoft.EntityFrameworkCore;
using TechLandier.Data;

namespace TechLandier.Features;

/// Sales analytics over the SalesDailyStat rollups plus live order lines. The admin pages load these as
/// parallel Inertia props, so every method runs on its own context.
public sealed class SalesService(IDbContextFactory<AppDb> dbs)
{
    static DateOnly Since(int days) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));
    static DateTime SinceUtc(int days) => Since(days).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public async Task<SalesKpis> KpisAsync(int days, CancellationToken ct)
    {
        await using var db = await dbs.CreateDbContextAsync(ct);
        var since = Since(days);
        var sinceUtc = SinceUtc(days);
        var totals = await db.SalesDailyStats.Where(s => s.Date >= since)
            .GroupBy(_ => 1)
            .Select(g => new { Revenue = g.Sum(s => s.Revenue), Orders = g.Sum(s => s.OrderCount), Bnpl = g.Sum(s => s.BnplShare), Pickups = g.Sum(s => s.PickupCount), Deliveries = g.Sum(s => s.DeliveryCount) })
            .FirstOrDefaultAsync(ct);
        var views = await db.ProductViews.CountAsync(v => v.At >= sinceUtc, ct);
        return new SalesKpis(totals?.Revenue ?? 0, totals?.Orders ?? 0, totals?.Bnpl ?? 0, totals?.Pickups ?? 0, totals?.Deliveries ?? 0, views);
    }

    public async Task<List<SeriesRow>> SeriesAsync(int days, CancellationToken ct)
    {
        await using var db = await dbs.CreateDbContextAsync(ct);
        var since = Since(days);
        return await db.SalesDailyStats.AsNoTracking()
            .Where(s => s.Date >= since)
            .OrderBy(s => s.Date)
            .Select(s => new SeriesRow(s.Date, s.Revenue, s.OrderCount, s.BnplShare, s.PickupCount, s.DeliveryCount))
            .ToListAsync(ct);
    }

    public async Task<List<TopProduct>> TopProductsAsync(int days, CancellationToken ct)
    {
        await using var db = await dbs.CreateDbContextAsync(ct);
        var sinceUtc = SinceUtc(days);
        return await db.OrderItems.AsNoTracking()
            .Where(i => i.Order.CreatedAt >= sinceUtc && OrderStatuses.PaidOrLater.Contains(i.Order.Status))
            .GroupBy(i => new { i.ProductId, i.Name })
            .OrderByDescending(g => g.Sum(i => i.LineTotal))
            .Take(8)
            .Select(g => new TopProduct(g.Key.Name, g.Sum(i => i.LineTotal), g.Sum(i => i.Qty)))
            .ToListAsync(ct);
    }

    public async Task<List<CategoryShare>> CategoryShareAsync(int days, CancellationToken ct)
    {
        await using var db = await dbs.CreateDbContextAsync(ct);
        var sinceUtc = SinceUtc(days);
        return await db.OrderItems.AsNoTracking()
            .Where(i => i.Order.CreatedAt >= sinceUtc && OrderStatuses.PaidOrLater.Contains(i.Order.Status))
            .GroupBy(i => i.Product != null ? i.Product.Category.Name : "Other")
            .OrderByDescending(g => g.Sum(i => i.LineTotal))
            .Take(6)
            .Select(g => new CategoryShare(g.Key, g.Sum(i => i.LineTotal)))
            .ToListAsync(ct);
    }
}
