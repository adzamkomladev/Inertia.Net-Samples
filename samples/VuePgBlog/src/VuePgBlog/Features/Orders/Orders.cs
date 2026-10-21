namespace VuePgBlog.Features;

public static class Orders
{
    public static void MapOrders(this IEndpointRouteBuilder app)
    {
        // Public tracking with an unguessable token (or owner); redirects guests to login at /login when no token.
        app.MapGet("/orders/{number}", OrderService.TrackAsync);
    }
}

public static class AccountOrders
{
    public static void MapAccountOrders(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account/orders").RequireAuthorization("verified");
        group.MapGet("/", OrderService.AccountOrdersAsync);
        group.MapGet("/{number}", OrderService.AccountOrderAsync);
    }
}
