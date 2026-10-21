using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Infrastructure;

namespace VuePgBlog.Features;

/// Sales analytics: charts over SalesDailyStat rollups plus live top sellers from order lines.
public static partial class SalesService
{
    internal static async Task<IResult> ReportAsync(string? range, AppDb db, CancellationToken ct)
    {
        var days = range == "90" ? 90 : range == "7" ? 7 : 30;
        var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));
        var sinceUtc = since.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var paidStatuses = new[] { OrderStatuses.Paid, OrderStatuses.Processing, OrderStatuses.Shipped, OrderStatuses.Delivered };

        return Render(
            "admin/Analytics",
            new InertiaProps
            {
                ["range"] = days,
                ["kpis"] = Defer(token => KpisAsync(db, since, paidStatuses, token), "kpis")
                    .Cache($"admin-analytics:{days}:kpis", TimeSpan.FromSeconds(60)),
                ["series"] = Defer(token => SeriesAsync(db, since, token), "charts")
                    .Cache($"admin-analytics:{days}:series", TimeSpan.FromSeconds(60)),
                ["topProducts"] = Defer(token => TopProductsAsync(db, sinceUtc, paidStatuses, token), "charts")
                    .Cache($"admin-analytics:{days}:topProducts", TimeSpan.FromSeconds(60)),
                ["categoryShare"] = Defer(token => CategoryShareAsync(db, sinceUtc, paidStatuses, token), "charts")
                    .Cache($"admin-analytics:{days}:categoryShare", TimeSpan.FromSeconds(60)),
            }
        );
    }

    static async Task<object> KpisAsync(AppDb db, DateOnly since, string[] paid, CancellationToken ct)
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

    static async Task<object> SeriesAsync(AppDb db, DateOnly since, CancellationToken ct)
    {
        var rows = await db.SalesDailyStats.AsNoTracking()
            .Where(s => s.Date >= since)
            .OrderBy(s => s.Date)
            .Select(s => new { s.Date, s.Revenue, s.OrderCount, s.BnplShare, s.PickupCount, s.DeliveryCount })
            .ToListAsync(ct);
        return rows;
    }

    static async Task<object> TopProductsAsync(AppDb db, DateTime sinceUtc, string[] paid, CancellationToken ct) =>
        await db.OrderItems.AsNoTracking()
            .Where(i => i.Order.CreatedAt >= sinceUtc && paid.Contains(i.Order.Status))
            .GroupBy(i => new { i.ProductId, i.Name })
            .OrderByDescending(g => g.Sum(i => i.LineTotal))
            .Take(8)
            .Select(g => new { Name = g.Key.Name, Revenue = g.Sum(i => i.LineTotal), Qty = g.Sum(i => i.Qty) })
            .ToListAsync(ct);

    static async Task<object> CategoryShareAsync(AppDb db, DateTime sinceUtc, string[] paid, CancellationToken ct) =>
        await db.OrderItems.AsNoTracking()
            .Where(i => i.Order.CreatedAt >= sinceUtc && paid.Contains(i.Order.Status))
            .GroupBy(i => i.Product != null ? i.Product.Category.Name : "Other")
            .OrderByDescending(g => g.Sum(i => i.LineTotal))
            .Take(6)
            .Select(g => new { Label = g.Key, Revenue = g.Sum(i => i.LineTotal) })
            .ToListAsync(ct);
}
