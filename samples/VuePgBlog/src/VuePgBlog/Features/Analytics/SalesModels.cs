namespace VuePgBlog.Features;

/// One row per day, rebuilt by the sales-rollup cron; backs admin KPIs and charts.
public sealed class SalesDailyStat
{
    public DateOnly Date { get; set; }
    /// Total revenue of paid-or-later orders that day (refunds excluded; none tracked).
    public decimal Revenue { get; set; }
    public int OrderCount { get; set; }
    /// Revenue that came through BNPL plans that day.
    public decimal BnplShare { get; set; }
    /// Pay-on-delivery orders that day.
    public int PickupCount { get; set; }
    /// Deliveries marked delivered that day.
    public int DeliveryCount { get; set; }
}

/// Aggregated, privacy-safe product view row written by the ViewEventWriter batch.
public sealed class ProductView
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    /// Salted hash of the viewer fingerprint — never a raw IP.
    public required string ViewerHash { get; set; }
    public string? Referrer { get; set; }
    public DateTime At { get; set; }
}
