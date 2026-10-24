using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using static TechLandier.Features.Pricing;

namespace TechLandier.Features;

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
    /// The price the storefront charges today, see Pricing.Display.
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

public sealed record CategoryTile(Guid Id, string Slug, string Name, string Tagline, string Icon, int Count);

public sealed record ZoneRow(Guid Id, string Name, decimal Fee, string EtaText, bool AllowPayOnDelivery);

public sealed record SpecRow(string Label, string Value);

public sealed record ReviewItem(Guid Id, string AuthorName, int Rating, string? Title, string Body, DateTime CreatedAt, bool VerifiedPurchase);

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
    List<SpecRow> Specs,
    List<string> Images)
{
    public bool HasDiscount => DiscountPercent > 0;
    public string? SavingsLabel => HasDiscount ? $"{Show(Price - DisplayPrice)} off ({DiscountPercent}%)" : null;
}

/// Query-string filters of the catalogue; Sort is n/new (newest), chigh (price high to low) or hlow (price low to high).
public sealed record ProductQuery(string? Q, string? Category, decimal? Min, decimal? Max, bool? InStock, string? Sort, int? Page)
{
    /// The canonical sort key, or null for an unknown one.
    public string? SortKey => Sort switch
    {
        null or "" or "n" or "new" => "new",
        "chigh" => "chigh",
        "hlow" => "hlow",
        _ => null,
    };
}
