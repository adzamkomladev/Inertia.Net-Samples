using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Store
{
    public static void MapStore(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", Home).WithName("home");
        app.MapGet("/products", Products).WithName("products.index");
        app.MapGet("/products/{slug}", Show).WithName("products.show");
        // Show.vue posts here after mount, so cached or prefetched visits still count as a view.
        app.MapPost("/products/{id:guid}/view", (Guid id, HttpContext ctx, ViewTracker views) =>
        {
            views.Track(ctx, id);
            return Results.Accepted();
        }).RequireRateLimiting("views").WithName("products.view");
        app.MapGet("/deals", Deals).WithName("deals.index");
        app.MapInertia("/about", "pages/About").WithName("pages.about");
        app.MapGet("/support/policy", async (StoreService store, CancellationToken ct) =>
            Render("pages/Policy", new InertiaProps { ["zones"] = await store.ActiveZonesAsync(ct) })).WithName("pages.policy");
    }

    // The three rails are independent queries, each on its own context, so they load together on the first paint (eager, so the server-rendered page has products).
    static IResult Home(StoreService store, ManagementService management) =>
        Render("Home", new InertiaProps
        {
            ["categories"] = Prop(store.CategoryTilesAsync).Parallel()
                .Cache("catalog:categories", StoreCache.CatalogFreshFor, StoreCache.CatalogStaleFor, [StoreCache.Catalog]),
            ["deals"] = Prop(store.DealsRailAsync).Parallel()
                .Cache("home:deals", StoreCache.RailFreshFor, StoreCache.RailStaleFor, [StoreCache.Home, StoreCache.Catalog]),
            ["arrivals"] = Prop(store.ArrivalsRailAsync).Parallel()
                .Cache("home:arrivals", StoreCache.RailFreshFor, StoreCache.RailStaleFor, [StoreCache.Home, StoreCache.Catalog]),
            ["settings"] = Prop(management.SettingsAsync),
        });

    static async Task<IResult> Products([AsParameters] ProductQuery query, StoreService store, CancellationToken ct) =>
        await RenderProductsAsync(query, store, ct);

    /// <summary>The products page render shared by GET /products and the login POST, which renders it inline after signing in.</summary>
    internal static async Task<IResult> RenderProductsAsync(ProductQuery query, StoreService store, CancellationToken ct)
    {
        if (query.SortKey is null) return Results.BadRequest("Invalid sort.");
        var products = await store.SearchAsync(query, ct);
        var result = Render(new ProductsPage(
            Scroll(products, "data", ScrollMetadata.FromPage(products.Page, products.HasMore)),
            new ProductFilters(query.Q ?? "", query.Category ?? "", query.Min, query.Max, query.InStock, query.SortKey),
            Prop(store.CategoryTilesAsync)
                .Cache("catalog:categories", StoreCache.CatalogFreshFor, StoreCache.CatalogStaleFor, [StoreCache.Catalog])));
        // A filtered/paged listing is not the canonical page: do not multiply shared SSR entries.
        var canonical = query.Q is null && query.Category is null && query.Min is null && query.Max is null
            && query.InStock is not true && (query.Page is null or 1);
        return canonical ? result : result.WithSsrCache(TimeSpan.Zero);
    }

    static async Task<IResult> Show(string slug, HttpContext ctx, StoreService store, ViewTracker views, CancellationToken ct)
    {
        var product = await store.DetailAsync(p => p.Slug == slug, ct);
        if (product is null) return RouteResults.NotFoundPage();
        views.Track(ctx, product.Id);
        return Render("products/Show", new InertiaProps
        {
            ["product"] = product,
            ["reviews"] = Defer(async c => await store.ReviewsAsync(product.Id, c), "reviews"),
            ["related"] = Defer(async c => await store.RelatedAsync(product.Id, product.CategoryId, c), "related")
                .Cache($"related:{product.Id}", StoreCache.RelatedFreshFor, StoreCache.RelatedStaleFor,
                    [StoreCache.Product(product.Id), StoreCache.Catalog]),
            ["canReview"] = Defer(async c => ctx.User.UserIdOrNull() is { } uid && await store.CanReviewAsync(product.Id, uid, c), "reviews"),
        });
    }

    static async Task<IResult> Deals([AsParameters] ProductQuery query, StoreService store, CancellationToken ct)
    {
        var products = await store.DealsAsync(query, ct);
        return Render(new DealsPage(
            Scroll(products, "data", ScrollMetadata.FromPage(products.Page, products.HasMore)),
            query.SortKey ?? "chigh"));
    }
}
