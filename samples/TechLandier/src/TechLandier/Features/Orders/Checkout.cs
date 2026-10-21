using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;


public static class Checkout
{
    public static void MapCheckout(this IEndpointRouteBuilder app)
    {
        // Guests land on the login page with the buybox URL as the return target (cookie handler).
        app.MapGet("/buy/{productId:guid}", BuyAsync).RequireAuthorization("verified");
        app.MapPost("/checkout", CreateAsync)
            .RequireAuthorization("verified")
            .WithInertiaValidation()
            .RequireRateLimiting("writes");
    }

    internal static async Task<IResult> BuyAsync(Guid productId, int? qty, string? bnpl, bool? insure, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var data = await CheckoutService.PrepareBuyAsync(productId, qty, bnpl, insure, ctx.User.UserId(), db, ct);
        if (data.NotFound)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (data.RedirectToLogin is { } login)
            return Results.Redirect(login);
        return Render("products/Buy", new InertiaProps
        {
            ["product"] = data.Product,
            ["qty"] = data.Qty,
            ["zones"] = data.Zones,
            ["insure"] = data.Insure,
            ["bnpl"] = data.Bnpl,
            ["creditState"] = data.CreditState,
        });
    }

    internal static async Task<IResult> CreateAsync(
        CheckoutInput input,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        RealtimeEvents realtime,
        StockAlerts alerts,
        IHostEnvironment env,
        CancellationToken ct)
    {
        var outcome = await CheckoutService.CreateAsync(input, ctx.User.UserId(), db, jobs, realtime, alerts, env, ct);
        if (outcome.RedirectUrl is { } url)
        {
            if (outcome.FlashType is { } redirectFlash) ctx.Inertia().Flash("toast", new Toast(redirectFlash, outcome.FlashMessage!));
            return Results.Redirect(url);
        }
        if (outcome.FlashType is { } type) return Back().WithFlash("toast", new Toast(type, outcome.FlashMessage!));
        return Back();
    }
}
