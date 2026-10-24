using TechLandier.Data;
using NpgsqlTypes;

namespace TechLandier.Features;

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
    /// Cover image: always the first of Images, denormalized for cards and lists.
    public Guid? ImageAssetId { get; set; }
    public Asset? ImageAsset { get; set; }
    public string? ImageUrl { get; set; }
    public List<ProductImage> Images { get; set; } = [];
    /// JSON [{"label":"Port","value":"USB-C"}]
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
    /// Generated column, see AppDb.
    public NpgsqlTsVector SearchVector { get; set; } = null!;
}

/// Gallery image of a product, in display order. The URL is resolved once when the image is attached.
public sealed class ProductImage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ProductId { get; set; }
    public Guid AssetId { get; set; }
    public required string Url { get; set; }
    public int SortOrder { get; set; }

    public const int MaxPerProduct = 8;
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

/// Single source of the final price: Price minus the percentage discount, rounded to cents.
