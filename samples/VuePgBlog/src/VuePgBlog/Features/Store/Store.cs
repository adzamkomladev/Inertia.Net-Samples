using VuePgBlog.Data;
using VuePgBlog.Infrastructure;

namespace VuePgBlog.Features;

public static class Store
{
    public static void MapStore(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", HomeAsync);
        app.MapGet("/products", ProductsAsync);
        app.MapGet("/products/{slug}", ShowAsync);
        // View beacon: Show.vue posts here after mount so cached/prefetched visits still count.
        app.MapPost("/products/{id:guid}/view", (Guid id, HttpContext ctx, ViewTracker views) =>
        {
            views.Track(ctx, id);
            return Results.Accepted();
        }).RequireRateLimiting("views");
        app.MapGet("/deals", DealsAsync);
        app.MapGet("/about", AboutAsync);
        app.MapGet("/support/policy", PolicyAsync);
    }

    static IResult NotFoundPage() => Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);

    // ---------------------------------------------------------------- home

    internal static IResult HomeAsync(HttpContext ctx, AppDb db, CancellationToken ct) =>
        Render("Home", new InertiaProps
        {
            ["categories"] = Defer(
                (CancellationToken c) => StoreService.GetCategoryTilesAsync(db, c),
                group: "rails"),
            ["deals"] = Defer(
                (CancellationToken c) => StoreService.GetDealsRailAsync(db, c),
                group: "rails"),
            ["arrivals"] = Defer(
                (CancellationToken c) => StoreService.GetArrivalsRailAsync(db, c),
                group: "rails"),
        });

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
        sort = StoreService.SortNormalized(sort);
        if (sort is null) return Results.BadRequest("Invalid sort.");
        var pageNo = Math.Max(page ?? 1, 1);
        var (cards, hasMore, engineMeili) = await StoreService.SearchProductsAsync(
            services.GetRequiredService<IServiceScopeFactory>(), db, q, category, min, max, inStock, sort!, pageNo, StoreService.PageSize, ct);
        return Render("products/Index", new InertiaProps
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
            ["categories"] = await StoreService.GetCategoryTilesAsync(db, ct),
            ["searchEngine"] = Always(engineMeili ? "meilisearch" : "native"),
        });
    }

    // ----------------------------------------------------------- product page

    internal static async Task<IResult> ShowAsync(string slug, HttpContext ctx, AppDb db, ViewTracker views, CancellationToken ct)
    {
        var uid = ctx.User.UserIdOrNull();
        var product = await StoreService.LoadDetail(db, slug, ct);
        if (product is null)
            return NotFoundPage();
        views.Track(ctx, product.Id);
        return Render("products/Show", new InertiaProps
        {
            ["product"] = product,
            ["reviews"] = Defer(
                (CancellationToken c) => StoreService.GetReviewsAsync(db, product.Id, c),
                group: "reviews"),
            ["related"] = Defer(
                (CancellationToken c) => StoreService.GetRelatedAsync(db, product.Id, product.CategoryId, c),
                group: "related"),
            ["canReview"] = Always(uid is not null && await StoreService.CanReviewAsync(db, product.Id, uid.Value, ct)),
        });
    }

    // ------------------------------------------------------------ deals rail

    internal static async Task<IResult> DealsAsync(string? sort, int? page, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var (products, sortKey) = await StoreService.GetDealsFeedAsync(db, sort, page, ct);
        return Render("deals/Index", new InertiaProps
        {
            ["products"] = Scroll(products, "data"),
            ["sort"] = sortKey,
        });
    }

    // ---------------------------------------------------------------- static

    internal static IResult AboutAsync(HttpContext ctx, AppDb db, CancellationToken ct) =>
        Render("pages/About");

    internal static async Task<IResult> PolicyAsync(HttpContext ctx, AppDb db, CancellationToken ct) =>
        Render("pages/Policy", new InertiaProps
        {
            ["zones"] = await StoreService.GetActiveZonesAsync(db, ct),
        });
}
