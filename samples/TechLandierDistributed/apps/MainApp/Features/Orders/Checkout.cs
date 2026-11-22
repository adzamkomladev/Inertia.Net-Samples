using System.Security.Claims;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Checkout
{
    public static void MapCheckout(this IEndpointRouteBuilder app)
    {
        // Guests land on the login page with the buy URL as the return target (cookie handler).
        app.MapGet("/buy/{productId:guid}", Buy).RequireAuthorization("verified").WithName("checkout.buy");
        app.MapPost("/checkout", async (CheckoutInput input, ClaimsPrincipal user, CheckoutService checkout, OrderService orders, HttpContext ctx, CancellationToken ct) =>
        {
            var result = await checkout.CreateAsync(input, user.UserId(), ct);
            // Playbook: a paid checkout renders the buyer's order page inline instead of redirecting to the guest
            // tracking page, which itself redirects a verified owner to the account page — one request instead of
            // three. Errors, non-Inertia posts and any other outcome keep the mapped SaveResult response.
            if (result.Errors is null && result.FlashType is { } flashType
                && result.RedirectUrl is { } url
                && url.StartsWith("/orders/", StringComparison.Ordinal)
                && ctx.InertiaRequest().IsInertia)
            {
                var number = url["/orders/".Length..].Split('?', 2)[0];
                ctx.Inertia().Flash("toast", new Toast(flashType, result.FlashMessage!));
                PageUrl.Next(ctx, $"/account/orders/{number}");
                return await Orders.RenderAccountOrderAsync(number, user.UserId(), orders, ct);
            }
            return (object?)result;
        })
        .RequireAuthorization("verified")
        .WithInertiaValidation()
        .RequireRateLimiting("writes")
        .WithName("checkout.store");
    }

    static async Task<IResult> Buy(Guid productId, int? qty, string? bnpl, bool? insure, ClaimsPrincipal user,
        StoreService store, CreditService credit, ManagementService management, CancellationToken ct)
    {
        var product = await store.DetailAsync(p => p.Id == productId, ct);
        if (product is null) return RouteResults.NotFoundPage();
        return Render("products/Buy", new InertiaProps
        {
            ["product"] = product,
            ["qty"] = Math.Clamp(qty ?? 1, 1, 10),
            ["zones"] = await store.ActiveZonesAsync(ct),
            ["settings"] = await management.SettingsAsync(ct),
            ["insure"] = insure ?? false,
            ["bnpl"] = bnpl,
            ["creditState"] = Defer(async c => await credit.StateAsync(user.UserId(), c), "credit"),
        });
    }
}
