using System.ComponentModel.DataAnnotations;
using static VuePgBlog.Features.Pricing;

namespace VuePgBlog.Features;

public sealed record ProductCard(
    Guid Id,
    string Slug,
    string Name,
    string Brand,
    string CategorySlug,
    /// Lucide icon name for ProductArt (Headphones, Cpu, ...).
    string Icon,
    decimal Price,
    decimal? CompareAtPrice,
    int DiscountPercent,
    /// Pricing.Display — the price the storefront charges today.
    decimal DisplayPrice,
    int Stock,
    int SoldCount,
    decimal RatingAvg,
    int RatingCount,
    string? ImageUrl)
{
    public bool InStock => Stock > 0;
    public int Savings => CompareAtPrice is { } was && was > DisplayPrice ? (int)Math.Round(was - DisplayPrice) : 0;
}

public sealed record CategoryTile(
    Guid Id,
    string Slug,
    string Name,
    string Tagline,
    string Icon,
    int Count);

/// Static pages content row for /support/policy: zones with fees + ETA + pay-on-delivery support.
public sealed record ZoneRow(string Name, decimal Fee, string EtaText, bool AllowPayOnDelivery);

public sealed record ProductFeedPage(List<ProductCard> Data, int Page, bool HasMore) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}

public sealed record SpecRow(string Label, string Value);

public sealed record ReviewItem(
    Guid Id,
    string AuthorName,
    int Rating,
    string? Title,
    string Body,
    DateTime CreatedAt,
    bool VerifiedPurchase);

public sealed record ProductFilterInput
{
    public string? Q { get; init; }
    public string? Category { get; init; }
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }
    public bool? InStock { get; init; }
    /// n = newest, chigh = price high→low, hlow = price low→high, new = newest alias.
    public string? Sort { get; init; }
}

public sealed record ProductDetail(
    Guid Id,
    string Slug,
    string Name,
    string Brand,
    Guid CategoryId,
    string CategorySlug,
    string CategoryName,
    string Icon,
    string Summary,
    string Description,
    decimal Price,
    decimal? CompareAtPrice,
    int DiscountPercent,
    decimal DisplayPrice,
    int Stock,
    int LowStockThreshold,
    int SoldCount,
    int WarrantyMonths,
    bool IsInsurable,
    decimal RatingAvg,
    int RatingCount,
    string? ImageUrl,
    List<SpecRow> Specs)
{
    public bool HasDiscount => DiscountPercent > 0;
    public string? SavingsLabel => HasDiscount
        ? $"{Show(Price - DisplayPrice)} off ({DiscountPercent}%)" : null;
}

public sealed record CheckoutInput
{
    [Required]
    public Guid? ProductId { get; init; }
    [Range(1, 10)]
    public int Qty { get; init; } = 1;
    public Guid? ZoneId { get; init; }
    /// card | mobile_money | pay_on_delivery
    [Required]
    public string? PaymentMethod { get; init; }
    /// null = full payment; 3 | 6 | 12 = BNPL installments every 14 days.
    public int? BnplCount { get; init; }
    /// Device insurance add-on for insurable products.
    public bool Insure { get; init; }
    /// Recipient snapshot.
    [Required, StringLength(80)]
    public string? RecipientName { get; init; }
    [Required, StringLength(24)]
    public string? Phone { get; init; }
    [Required, StringLength(120)]
    public string? StreetLabel { get; init; }
    public string? Notes { get; init; }
    /// Idempotency key from the client (uuid); a retry with the same key returns the same order.
    [Required, StringLength(200)]
    public string? CheckoutAttemptId { get; init; }
}
