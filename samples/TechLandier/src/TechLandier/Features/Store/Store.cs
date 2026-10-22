using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Store
{
    static readonly TimeSpan RailTtl = TimeSpan.FromMinutes(1);

    public static void MapStore(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", Home);
        app.MapGet("/products", Products);
        app.MapGet("/products/{slug}", Show);
        // Show.vue posts here after mount, so cached or prefetched visits still count as a view.
        app.MapPost("/products/{id:guid}/view", (Guid id, HttpContext ctx, ViewTracker views) =>
        {
            views.Track(ctx, id);
            return Results.Accepted();
        }).RequireRateLimiting("views");
        app.MapGet("/deals", Deals);
        app.MapInertia("/about", "pages/About");
        app.MapGet("/support/policy", async (StoreService store, CancellationToken ct) =>
            Render("pages/Policy", new InertiaProps { ["zones"] = await store.ActiveZonesAsync(ct) }));
    }

    // The three rails are independent queries, each on its own context, so they load together on the first paint (eager, so the server-rendered page has products).
    static IResult Home(StoreService store, ManagementService management) =>
        Render("Home", new InertiaProps
        {
            ["categories"] = Prop(store.CategoryTilesAsync).Parallel().Cache("catalog:categories", TimeSpan.FromMinutes(5)),
            ["deals"] = Prop(store.DealsRailAsync).Parallel().Cache("home:deals", RailTtl),
            ["arrivals"] = Prop(store.ArrivalsRailAsync).Parallel().Cache("home:arrivals", RailTtl),
            ["settings"] = Prop(management.SettingsAsync),
        });

    static async Task<IResult> Products([AsParameters] ProductQuery query, StoreService store, CancellationToken ct)
    {
        if (query.SortKey is null) return Results.BadRequest("Invalid sort.");
        return Render("products/Index", new InertiaProps
        {
            ["products"] = Scroll(await store.SearchAsync(query, ct), "data"),
            ["filters"] = new
            {
                q = query.Q ?? "",
                category = query.Category ?? "",
                query.Min,
                query.Max,
                query.InStock,
                sort = query.SortKey,
            },
            ["categories"] = Prop(store.CategoryTilesAsync).Cache("catalog:categories", TimeSpan.FromMinutes(5)),
        });
    }

    static async Task<IResult> Show(string slug, HttpContext ctx, StoreService store, ViewTracker views, CancellationToken ct)
    {
        var product = await store.DetailAsync(p => p.Slug == slug, ct);
        if (product is null) return HttpExtensions.NotFoundPage();
        views.Track(ctx, product.Id);
        return Render("products/Show", new InertiaProps
        {
            ["product"] = product,
            ["reviews"] = Defer(async c => await store.ReviewsAsync(product.Id, c), "reviews"),
            ["related"] = Defer(async c => await store.RelatedAsync(product.Id, product.CategoryId, c), "related")
                .Cache($"related:{product.Id}", TimeSpan.FromMinutes(2)),
            ["canReview"] = Always(ctx.User.UserIdOrNull() is { } uid && await store.CanReviewAsync(product.Id, uid, ct)),
        });
    }

    static async Task<IResult> Deals([AsParameters] ProductQuery query, StoreService store, CancellationToken ct) =>
        Render("deals/Index", new InertiaProps
        {
            ["products"] = Scroll(await store.DealsAsync(query, ct), "data"),
            ["sort"] = query.SortKey ?? "chigh",
        });
}
