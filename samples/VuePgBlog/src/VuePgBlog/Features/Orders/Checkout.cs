using VuePgBlog.Data;

namespace VuePgBlog.Features;

public static class Checkout
{
    public static void MapCheckout(this IEndpointRouteBuilder app)
    {
        // Guests land on the login page with the buybox URL as the return target (cookie handler).
        app.MapGet("/buy/{productId:guid}", (Guid productId, int? qty, string? bnpl, bool? insure, HttpContext ctx, AppDb db, CancellationToken ct)
                => CheckoutService.BuyAsync(productId, qty, bnpl, insure, ctx, db, ct))
            .RequireAuthorization("verified");
        app.MapPost("/checkout", CheckoutService.CreateAsync)
            .RequireAuthorization("verified")
            .WithInertiaValidation()
            .RequireRateLimiting("writes");
    }
}
