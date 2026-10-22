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
    List<SpecRow> Specs)
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

/// Storefront reads: home rails, catalogue search and filters, product detail, deals and static pages.
/// The rail loaders run as parallel Inertia props, so they open their own context instead of using the request's.
public sealed class StoreService(AppDb db, IDbContextFactory<AppDb> dbs)
{
    public const int PageSize = 12;

    // Cards are projected straight from EF so no entity is materialized per row.
    static IQueryable<ProductCard> Cards(IQueryable<Product> products) =>
        products.Select(p => new ProductCard(
            p.Id, p.Slug, p.Name, p.Brand, p.Category.Slug, p.Category.Icon, p.Price, p.CompareAtPrice, p.DiscountPercent,
            Display(p.Price, p.DiscountPercent), p.Stock, p.SoldCount, p.RatingAvg, p.RatingCount, p.ImageUrl));

    /// Full-text match on name/brand/keywords/summary plus trigram matching on the name, so substrings
    /// ("pho") and typos ("headfones") still find products. Both are served by GIN indexes.
    public static IQueryable<Product> Matching(IQueryable<Product> products, string term)
    {
        var like = $"%{term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
        return products.Where(p =>
            p.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("english", term))
            || EF.Functions.ILike(p.Name, like)
            || EF.Functions.TrigramsAreSimilar(p.Name, term));
    }

    static IOrderedQueryable<Product> Sorted(IQueryable<Product> query, string sort, string? term) => sort switch
    {
        "chigh" => query.OrderByDescending(p => p.Price * (100 - p.DiscountPercent) / 100m).ThenByDescending(p => p.Id),
        "hlow" => query.OrderBy(p => p.Price * (100 - p.DiscountPercent) / 100m).ThenByDescending(p => p.Id),
        _ when term is not null => query
            .OrderByDescending(p => p.SearchVector.Rank(EF.Functions.WebSearchToTsQuery("english", term)))
            .ThenByDescending(p => EF.Functions.TrigramsSimilarity(p.Name, term))
            .ThenByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id),
        _ => query.OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id),
    };

    public Task<Paged<ProductCard>> SearchAsync(ProductQuery f, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking();
        var term = f.Q?.Trim() is { Length: > 0 } t ? t : null;
        if (term is not null) query = Matching(query, term);
        if (f.Category is { Length: > 0 } slug) query = query.Where(p => p.Category.Slug == slug);
        if (f.Min is { } lo) query = query.Where(p => p.Price * (100 - p.DiscountPercent) / 100m >= lo);
        if (f.Max is { } hi) query = query.Where(p => p.Price * (100 - p.DiscountPercent) / 100m <= hi);
        if (f.InStock == true) query = query.Where(p => p.Stock > 0);
        return Cards(Sorted(query, f.SortKey!, term)).ToPagedAsync(f.Page, PageSize, ct);
    }

    /// Deals feed; an unknown sort falls back to the biggest prices first.
    public Task<Paged<ProductCard>> DealsAsync(ProductQuery f, CancellationToken ct) =>
        Cards(Sorted(db.Products.AsNoTracking().Where(p => p.DiscountPercent > 0), f.SortKey ?? "chigh", null)).ToPagedAsync(f.Page, PageSize, ct);

    // ------------------------------------------------------------ product page

    public Task<ProductDetail?> DetailAsync(Expression<Func<Product, bool>> where, CancellationToken ct) =>
        db.Products.AsNoTracking()
            .Where(where)
            .Select(p => new ProductDetail(
                p.Id, p.Slug, p.Name, p.Brand, p.CategoryId, p.Category.Slug, p.Category.Name, p.Category.Icon, p.Summary,
                p.Description, p.Price, p.CompareAtPrice, p.DiscountPercent, Display(p.Price, p.DiscountPercent), p.Stock,
                p.LowStockThreshold, p.SoldCount, p.WarrantyMonths, p.IsInsurable, p.RatingAvg, p.RatingCount, p.ImageUrl,
                ParseSpecs(p.SpecsJson)))
            .FirstOrDefaultAsync(ct);

    static List<SpecRow> ParseSpecs(string json)
    {
        try { return JsonSerializer.Deserialize<List<SpecRow>>(json, JsonSerializerOptions.Web) ?? []; }
        catch (JsonException) { return []; }
    }

    public Task<List<ReviewItem>> ReviewsAsync(Guid productId, CancellationToken ct) =>
        db.Reviews.AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == ReviewStatuses.Published)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Take(30)
            .Select(r => new ReviewItem(r.Id, r.User.Name, r.Rating, r.Title, r.Body, r.CreatedAt, r.OrderItem != null))
            .ToListAsync(ct);

    public async Task<List<ProductCard>> RelatedAsync(Guid productId, Guid categoryId, CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await Cards(own.Products.AsNoTracking().Where(p => p.CategoryId == categoryId && p.Id != productId)
            .OrderByDescending(p => p.RatingAvg).ThenByDescending(p => p.SoldCount).Take(6)).ToListAsync(ct);
    }

    /// True while the user has not reviewed the product yet (hidden reviews still count).
    public async Task<bool> CanReviewAsync(Guid productId, Guid uid, CancellationToken ct) =>
        !await db.Reviews.IgnoreQueryFilters().AnyAsync(r => r.ProductId == productId && r.UserId == uid, ct);

    // ---------------------------------------------------------------- home rails

    public async Task<List<CategoryTile>> CategoryTilesAsync(CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await own.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new CategoryTile(c.Id, c.Slug, c.Name, c.Tagline, c.Icon, own.Products.Count(p => p.CategoryId == c.Id)))
            .ToListAsync(ct);
    }

    public async Task<List<ProductCard>> DealsRailAsync(CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await Cards(own.Products.AsNoTracking().Where(p => p.DiscountPercent > 0 && p.Stock > 0)
            .OrderByDescending(p => p.DiscountPercent).ThenByDescending(p => p.PublishedAt).Take(8)).ToListAsync(ct);
    }

    public async Task<List<ProductCard>> ArrivalsRailAsync(CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await Cards(own.Products.AsNoTracking().OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id).Take(8)).ToListAsync(ct);
    }

    // ---------------------------------------------------------------- static

    public Task<List<ZoneRow>> ActiveZonesAsync(CancellationToken ct) =>
        db.DeliveryZones.AsNoTracking()
            .Where(z => z.IsActive)
            .OrderBy(z => z.SortOrder)
            .Select(z => new ZoneRow(z.Id, z.Name, z.Fee, z.EtaText, z.AllowPayOnDelivery))
            .ToListAsync(ct);
}
