using VuePgBlog.Data;
namespace VuePgBlog.Features;

public static class ProductStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Archived = "archived";
}

public static class StockReasons
{
    public const string Restock = "restock";
    public const string Sale = "sale";
    public const string Release = "release";
    public const string Adjustment = "adjustment";
    public const string Damage = "damage";
}

public sealed class Category
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public string Tagline { get; set; } = "";
    /// Lucide icon name rendered by the client (Headphones, Cpu, Camera, ...).
    public required string Icon { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? DemoKey { get; set; }
}

public sealed class Product
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public required string Brand { get; set; }
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public string Summary { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public int DiscountPercent { get; set; }
    public Guid? ImageAssetId { get; set; }
    public Asset? ImageAsset { get; set; }
    public string? ImageUrl { get; set; }
    /** [{"label":"Port","value":"USB-C"}] */
    public string SpecsJson { get; set; } = "[]";
    /// 12/24/36/0 (0 = no warranty).
    public int WarrantyMonths { get; set; }
    public bool IsInsurable { get; set; }
    public decimal RatingAvg { get; set; }
    public int RatingCount { get; set; }
    public int SoldCount { get; set; }
    public int Stock { get; set; }
    public int LowStockThreshold { get; set; } = 5;
    public string Status { get; set; } = ProductStatuses.Draft;
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? Keywords { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public string? DemoKey { get; set; }
    public long IndexVersion { get; set; } = 1;
}

public sealed class StockMovement
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public required int Delta { get; set; }
    public required string Reason { get; set; }
    public string? Note { get; set; }
    public Guid? OrderId { get; set; }
    public Order? Order { get; set; }
    public Guid? ByUserId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}

/// Single source of the final price: Price minus Precentage discount, rounded to cents.
public static class Pricing
{
    /// BNPL fee baked into the installment total (documented in README; intentionally a constant).
    public const decimal BnplFeePercent = 5m;
    public const string Currency = "$";
    public static readonly decimal InsurancePremiumPercent = 0.15m;
    public static readonly decimal InsuranceCoveragePercent = 0.90m;

    public static int ClampDiscount(int percent) => Math.Clamp(percent, 0, 70);

    public static decimal Display(decimal price, int discountPercent) =>
        price * (100 - ClampDiscount(discountPercent)) / 100m is { } raw ? Math.Round(raw, 2, MidpointRounding.AwayFromZero) : 0m;

    public static string Show(decimal amount) =>
        Currency + amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
