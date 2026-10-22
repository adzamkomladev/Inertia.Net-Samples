using System.Security.Claims;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Checkout
{
    public static void MapCheckout(this IEndpointRouteBuilder app)
    {
        // Guests land on the login page with the buy URL as the return target (cookie handler).
        app.MapGet("/buy/{productId:guid}", Buy).RequireAuthorization("verified");
        app.MapPost("/checkout", async (CheckoutInput input, ClaimsPrincipal user, CheckoutService checkout, CancellationToken ct) =>
                await checkout.CreateAsync(input, user.UserId(), ct))
            .RequireAuthorization("verified")
            .WithInertiaValidation()
            .RequireRateLimiting("writes");
    }

    static async Task<IResult> Buy(Guid productId, int? qty, string? bnpl, bool? insure, ClaimsPrincipal user,
        StoreService store, CreditService credit, CancellationToken ct)
    {
        var product = await store.DetailAsync(p => p.Id == productId, ct);
        if (product is null) return HttpExtensions.NotFoundPage();
        return Render("products/Buy", new InertiaProps
        {
            ["product"] = product,
            ["qty"] = Math.Clamp(qty ?? 1, 1, 10),
            ["zones"] = await store.ActiveZonesAsync(ct),
            ["insure"] = insure ?? false,
            ["bnpl"] = bnpl,
            ["creditState"] = await credit.StateAsync(user.UserId(), ct),
        });
    }
}
