namespace VuePgBlog.Features;

public static class Store
{
    public static void MapStore(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", StoreService.HomeAsync);
        app.MapGet("/products", StoreService.ProductsAsync);
        app.MapGet("/products/{slug}", StoreService.ShowAsync);
        // View beacon: Show.vue posts here after mount so cached/prefetched visits still count.
        app.MapPost("/products/{id:guid}/view", (Guid id, HttpContext ctx, ViewTracker views) =>
        {
            views.Track(ctx, id);
            return Results.Accepted();
        }).RequireRateLimiting("views");
        app.MapGet("/deals", StoreService.DealsAsync);
        app.MapGet("/about", StoreService.AboutAsync);
        app.MapGet("/support/policy", StoreService.PolicyAsync);
    }
}
