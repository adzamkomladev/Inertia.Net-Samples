using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// Create/edit payload for catalog items. Specs is a JSON array of {label, value}; stock only moves through the ledger.
public sealed record ProductInput(
    string? Name,
    string? Brand,
    Guid CategoryId,
    string? Summary,
    string? Description,
    decimal? Price,
    int? DiscountPercent,
    int? Stock,
    int? LowStockThreshold,
    int? WarrantyMonths,
    bool? IsInsurable,
    string? Keywords,
    string? Specs,
    /// Gallery in display order; the first is the cover. Null leaves the images untouched.
    List<Guid>? ImageAssetIds,
    string? Status);

public sealed record RestockInput(int Delta, string? Note);
public sealed record CategoryInput(string? Name, string? Tagline, string? Icon, bool? IsActive);

public sealed record OverviewKpis(decimal Revenue, int Orders, decimal Aov, decimal PlanExposure);
public sealed record LowStockRow(Guid Id, string Name, string Brand, int Stock, int LowStockThreshold);
public sealed record Attention(int OrdersToConfirm, int DeliveriesToAssign, int OverdueTickets, int LowStockCount, int WarrantyClaims, int InsuranceClaims, int UnverifiedUsers);

public sealed record AdminProductRow(Guid Id, string Name, string Brand, string Slug, string Category, string CategoryIcon, decimal Price, int DiscountPercent,
    int Stock, int LowStockThreshold, int SoldCount, string Status, bool IsInsurable, int WarrantyMonths, string? ImageUrl, DateTime UpdatedAt);
public sealed record ProductImageRow(Guid AssetId, string Url);
public sealed record StockMovementRow(Guid Id, int Delta, string Reason, string? Note, string? OrderNumber, string? By, DateTime At);
public sealed record AdminCategoryRow(Guid Id, string Name, string Slug, string Icon, string Tagline, bool IsActive);
public sealed record EditingProduct(Guid Id, string Name, string Brand, Guid CategoryId, string Summary, string Description, decimal Price,
    int DiscountPercent, int Stock, int LowStockThreshold, int WarrantyMonths, bool IsInsurable, string? Keywords, string Status, string SpecsJson,
    List<ProductImageRow> Images);
/// Admin product page: the editable fields plus what the store knows about the product.
public sealed record AdminProductDetail(EditingProduct Product, string Slug, string Category, string CategoryIcon, int SoldCount, decimal RatingAvg,
    int RatingCount, DateTime CreatedAt, DateTime UpdatedAt, DateTime? ArchivedAt);
public sealed record CatalogCounts(int All, int Active, int Draft, int Archived, int LowStock);
public sealed record CatalogData(Paged<AdminProductRow> Products, List<AdminCategoryRow> Categories, CatalogCounts Counts);
