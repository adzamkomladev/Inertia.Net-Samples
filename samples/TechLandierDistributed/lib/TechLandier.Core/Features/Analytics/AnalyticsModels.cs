using Microsoft.EntityFrameworkCore;
using TechLandier.Data;

namespace TechLandier.Features;

/// One row per day, rebuilt by the sales-rollup cron; backs the admin KPIs and charts.
public sealed class SalesDailyStat
{
    public DateOnly Date { get; set; }
    /// Revenue of paid-or-later orders that day.
    public decimal Revenue { get; set; }
    public int OrderCount { get; set; }
    /// Revenue that came through BNPL plans that day.
    public decimal BnplShare { get; set; }
    /// Pay-on-delivery orders that day.
    public int PickupCount { get; set; }
    /// Deliveries marked delivered that day.
    public int DeliveryCount { get; set; }
}

/// Privacy-safe product view row written in batches by the ViewEventWriter.
public sealed class ProductView
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    /// Salted hash of the viewer fingerprint, never a raw IP.
    public required string ViewerHash { get; set; }
    public string? Referrer { get; set; }
    public DateTime At { get; set; }
}

public sealed record SalesKpis(decimal Revenue, int Orders, decimal BnplShare, int Pickups, int Deliveries, int ProductViews);
public sealed record SeriesRow(DateOnly Date, decimal Revenue, int OrderCount, decimal BnplShare, int PickupCount, int DeliveryCount);
public sealed record TopProduct(string Name, decimal Revenue, int Qty);
public sealed record CategoryShare(string Label, decimal Revenue);
