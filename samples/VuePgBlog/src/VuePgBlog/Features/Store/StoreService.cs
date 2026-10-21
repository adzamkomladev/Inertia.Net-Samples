using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Infrastructure;

namespace VuePgBlog.Features;

/// Storefront reads: home rails, catalogue with filters + search (dual engine), product detail, deals, static pages.
public static partial class StoreService
{
    const int PageSize = 12;

    /// Cards are projected straight from EF so no full entity is materialized per row.
    internal static IQueryable<ProductCard> Cards(IQueryable<Product> products) =>
        products.Select(p => new ProductCard(
            p.Id,
            p.Slug,
            p.Name,
            p.Brand,
            p.Category.Slug,
            p.Category.Icon,
            p.Price,
            p.CompareAtPrice,
            p.DiscountPercent,
            Pricing.Display(p.Price, p.DiscountPercent),
            p.Stock,
            p.SoldCount,
            p.RatingAvg,
            p.RatingCount,
            p.ImageUrl));

    internal static string? SortNormalized(string? sort) => sort switch
    {
        "n" or "new" => "new", // newest ("new" is an accepted alias)
        "chigh" => "chigh",    // price high → low
        "hlow" => "hlow",      // price low → high
        null or "" => "new",
        _ => null,
    };

    static IQueryable<Product> Base(AppDb db) => db.Products.AsNoTracking();

    /// The effective storefront price is always Pricing.Display; computed inline so EF translates it to SQL.
    internal static IQueryable<Product> ApplyFilters(IQueryable<Product> query, string? categorySlug, decimal? min, decimal? max, bool? inStock)
    {
        if (categorySlug is { Length: > 0 }) query = query.Where(p => p.Category.Slug == categorySlug);
        if (min is { } lo) query = query.Where(p => p.Price * (100 - p.DiscountPercent) / 100m >= lo);
        if (max is { } hi) query = query.Where(p => p.Price * (100 - p.DiscountPercent) / 100m <= hi);
        if (inStock == true) query = query.Where(p => p.Stock > 0);
        return query;
    }

    static IOrderedQueryable<Product> ApplySort(IQueryable<Product> query, string sort) => sort switch
    {
        "chigh" => query.OrderByDescending(p => p.Price * (100 - p.DiscountPercent) / 100m).ThenByDescending(p => p.Id),
        "hlow" => query.OrderBy(p => p.Price * (100 - p.DiscountPercent) / 100m).ThenByDescending(p => p.Id),
        _ => query.OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id),
    };

    /// Full-text-ish native fallback (ILIKE over name/brand/summary/keywords), mirroring the sample's FTS idea.
    static IQueryable<Product> NativeMatch(IQueryable<Product> query, string q)
    {
        var pattern = $"%{q.Trim()}%";
        return query.Where(p =>
            EF.Functions.ILike(p.Name, pattern)
            || EF.Functions.ILike(p.Brand, pattern)
            || EF.Functions.ILike(p.Summary, pattern)
            || (p.Keywords != null && EF.Functions.ILike(p.Keywords, pattern)));
    }

    /// Re-fetches cards for rows already sorted, keeping the sort/hit order intact.
    static async Task<List<ProductCard>> LoadCards(AppDb db, List<Product> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var ids = rows.Select(p => p.Id).ToList();
        var cards = await Cards(Base(db).Where(p => ids.Contains(p.Id))).ToListAsync(ct);
        var byId = cards.ToDictionary(c => c.Id);
        return rows.Where(p => byId.ContainsKey(p.Id)).Select(p => byId[p.Id]).ToList();
    }

    /// <summary>
    /// Dual-engine search: Meilisearch when it is configured and fully reconciled, native ILIKE otherwise.
    /// Meili owns match order for default "newest"; an explicit price sort re-orders the hit set client-side.
    /// </summary>
    internal static async Task<(List<ProductCard> cards, bool hasMore, bool engineMeili)> SearchProductsAsync(
        IServiceScopeFactory scopes,
        AppDb db,
        string? q,
        string? categorySlug,
        decimal? min,
        decimal? max,
        bool? inStock,
        string sort,
        int pageNo,
        int size,
        CancellationToken ct)
    {
        var matched = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        List<Guid>? meiliIds = null;
        if (matched is not null)
        {
            await using var scope = scopes.CreateAsyncScope();
            meiliIds = await ProbeMeiliAsync(db, scope.ServiceProvider, matched, ct);
        }

        if (meiliIds is { Count: > 0 })
        {
            var query = ApplyFilters(Base(db).Where(p => meiliIds.Contains(p.Id)), categorySlug, min, max, inStock);
            var rows = await query.ToListAsync(ct);
            var cards = await LoadCards(db, rows, ct);
            if (sort != "new")
                cards = Reorder(cards, sort);
            else
                cards = cards.Where(c => meiliIds.Contains(c.Id)).ToList(); // keep Meili relevance order
            if (cards.Count == 0) return ([], false, true);
            var pageNoIndex = (pageNo - 1) * size;
            return (cards.Skip(pageNoIndex).Take(size).ToList(), cards.Count > pageNo * size, true);
        }

        var native = Base(db);
        if (matched is not null) native = NativeMatch(native, matched);
        native = ApplyFilters(native, categorySlug, min, max, inStock);
        var paged = await ApplySort(native, sort)
            .Skip((pageNo - 1) * size).Take(size + 1)
            .ToListAsync(ct);
        return (await LoadCards(db, paged, ct), paged.Count > size, false);
    }

    static List<ProductCard> Reorder(List<ProductCard> cards, string sort) => sort switch
    {
        "chigh" => cards.OrderByDescending(c => c.DisplayPrice).ThenBy(c => c.Name).ToList(),
        "hlow" => cards.OrderBy(c => c.DisplayPrice).ThenBy(c => c.Name).ToList(),
        _ => cards,
    };

    /// <summary>When Meilisearch is configured and the index is reconciled, returns matching product ids (best-match order). Null otherwise.</summary>
    internal static async Task<List<Guid>?> ProbeMeiliAsync(AppDb db, IServiceProvider services, string q, CancellationToken ct)
    {
        var client = services.GetService<MeilisearchTaskClient>();
        if (client is null
            || !string.Equals(services.GetRequiredService<IConfiguration>()["SEARCH_DRIVER"], "meilisearch", StringComparison.OrdinalIgnoreCase))
            return null;
        // Only serve from the index when it is fully reconciled, like the sample's feed does.
        if (await db.SearchIndexStates.AnyAsync(state => state.RequestedVersion > state.AppliedVersion, ct)
            || await db.Jobs.AnyAsync(job =>
                job.Type == nameof(ReindexSearchBatch)
                && (job.Status == JobStatus.Pending || job.Status == JobStatus.Running), ct)
            || await db.Products.AnyAsync(product => !db.SearchIndexStates.Any(state => state.ProductId == product.Id), ct))
            return null;
        try
        {
            var ns = services.GetRequiredService<OptionalServiceNamespace>();
            var result = await client.SearchAsync(ns.MeilisearchIndex("products"), q, 100, 0, ct);
            return result.DocumentIds
                .Select(id => Guid.TryParse(id, out var guid) ? guid : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return null; // Meili outage: fall through to the native engine.
        }
    }

    internal static async Task<ProductDetail?> LoadDetail(AppDb db, string slug, CancellationToken ct) =>
        await db.Products.AsNoTracking()
            .Where(p => p.Slug == slug)
            .Select(p => new ProductDetail(
                p.Id,
                p.Slug,
                p.Name,
                p.Brand,
                p.CategoryId,
                p.Category.Slug,
                p.Category.Name,
                p.Category.Icon,
                p.Summary,
                p.Description,
                p.Price,
                p.CompareAtPrice,
                p.DiscountPercent,
                Pricing.Display(p.Price, p.DiscountPercent),
                p.Stock,
                p.LowStockThreshold,
                p.SoldCount,
                p.WarrantyMonths,
                p.IsInsurable,
                p.RatingAvg,
                p.RatingCount,
                p.ImageUrl,
                SearchSpecs(p.SpecsJson)
            ))
            .FirstOrDefaultAsync(ct);

    static List<SpecRow> SearchSpecs(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<SpecRow>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    // ---------------------------------------------------------------- home

    internal static async Task<IResult> HomeAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        return Render(
            "Home",
            new InertiaProps
            {
                ["categories"] = Defer(
                    async (CancellationToken c) =>
                        (await db.Categories.AsNoTracking()
                            .Where(cat => cat.IsActive)
                            .OrderBy(cat => cat.SortOrder)
                            .Select(cat => new
                            {
                                cat.Id,
                                cat.Slug,
                                cat.Name,
                                cat.Tagline,
                                cat.Icon,
                                Count = db.Products.Where(p => p.CategoryId == cat.Id).Count(),
                            })
                            .ToListAsync(c))
                            .Select(r => new CategoryTile(r.Id, r.Slug, r.Name, r.Tagline, r.Icon, r.Count))
                            .ToList(),
                    group: "rails"
                ),
                ["deals"] = Defer(
                    async (CancellationToken c) =>
                        await Cards(Base(db).Where(p => p.DiscountPercent > 0 && p.Stock > 0)
                                .OrderByDescending(p => p.DiscountPercent).ThenByDescending(p => p.PublishedAt)
                                .Take(8))
                            .ToListAsync(c),
                    group: "rails"
                ),
                ["arrivals"] = Defer(
                    async (CancellationToken c) =>
                        await Cards(Base(db)
                                .OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id)
                                .Take(8))
                            .ToListAsync(c),
                    group: "rails"
                ),
            }
        );
    }

    // ------------------------------------------------------------ products

    internal static async Task<IResult> ProductsAsync(
        string? q,
        string? category,
        decimal? min,
        decimal? max,
        bool? inStock,
        string? sort,
        int? page,
        HttpContext ctx,
        AppDb db,
        IServiceProvider services,
        CancellationToken ct)
    {
        sort = SortNormalized(sort);
        if (sort is null) return Results.BadRequest("Invalid sort.");
        var pageNo = Math.Max(page ?? 1, 1);
        var (cards, hasMore, engineMeili) = await SearchProductsAsync(
            services.GetRequiredService<IServiceScopeFactory>(), db, q, category, min, max, inStock, sort!, pageNo, PageSize, ct);
        return Render(
            "products/Index",
            new InertiaProps
            {
                ["products"] = Scroll(new ProductFeedPage(cards, pageNo, hasMore), "data"),
                ["filters"] = new
                {
                    q = q ?? "",
                    category = category ?? "",
                    min = min,
                    max = max,
                    inStock = inStock,
                    sort = sort,
                },
                ["searchEngine"] = Always(engineMeili ? "meilisearch" : "native"),
            }
        );
    }

    // ----------------------------------------------------------- product page

    internal static async Task<IResult> ShowAsync(string slug, HttpContext ctx, AppDb db, ViewTracker views, CancellationToken ct)
    {
        var uid = ctx.User.UserIdOrNull();
        var product = await LoadDetail(db, slug, ct);
        if (product is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        views.Track(ctx, product.Id);
        return Render(
            "products/Show",
            new InertiaProps
            {
                ["product"] = product,
                ["reviews"] = Defer(
                    (CancellationToken c) => GetReviews(db, product.Id, c),
                    group: "reviews"
                ),
                ["related"] = Defer(
                    (CancellationToken c) =>
                        Cards(Base(db).Where(p => p.CategoryId == product.CategoryId && p.Id != product.Id)
                                .OrderByDescending(p => p.RatingAvg).ThenByDescending(p => p.SoldCount)
                                .Take(6))
                            .ToListAsync(c),
                    group: "related"
                ),
                ["canReview"] = Always(uid is not null && !await db.Reviews.IgnoreQueryFilters().AnyAsync(r => r.ProductId == product.Id && r.UserId == uid.Value, ct)),
            }
        );
    }

    static async Task<List<ReviewItem>> GetReviews(AppDb db, Guid productId, CancellationToken ct) =>
        await db.Reviews.AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == ReviewStatuses.Published)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Take(30)
            .Select(r => new ReviewItem(r.Id, r.User.Name, r.Rating, r.Title, r.Body, r.CreatedAt, r.OrderItem != null))
            .ToListAsync(ct);

    // ------------------------------------------------------------ deals rail

    internal static async Task<IResult> DealsAsync(string? sort, int? page, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        sort = SortNormalized(sort) ?? "chigh";
        var pageNo = Math.Max(page ?? 1, 1);
        var rows = await ApplySort(Base(db).Where(p => p.DiscountPercent > 0), sort!)
            .Skip((pageNo - 1) * PageSize).Take(PageSize + 1)
            .ToListAsync(ct);
        return Render(
            "deals/Index",
            new InertiaProps
            {
                ["products"] = Scroll(new ProductFeedPage(await LoadCards(db, rows, ct), pageNo, rows.Count > PageSize), "data"),
                ["sort"] = sort,
            }
        );
    }

    // ---------------------------------------------------------------- static

    internal static IResult AboutAsync(HttpContext ctx, AppDb db, CancellationToken ct) =>
        Render("pages/About");

    internal static async Task<IResult> PolicyAsync(HttpContext ctx, AppDb db, CancellationToken ct) =>
        Render(
            "pages/Policy",
            new InertiaProps
            {
                ["zones"] = await db.DeliveryZones.AsNoTracking()
                    .Where(z => z.IsActive)
                    .OrderBy(z => z.SortOrder)
                    .Select(z => new ZoneRow(z.Name, z.Fee, z.EtaText, z.AllowPayOnDelivery))
                    .ToListAsync(ct),
            }
        );
}
