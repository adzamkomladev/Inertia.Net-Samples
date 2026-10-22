using CarParker.Infrastructure;

namespace CarParker.Features.Lots;

public static class Lots
{
    public static void MapLots(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", HomeAsync);
        app.MapGet("/lots", IndexAsync);
        app.MapGet("/lots/{id:guid}", ShowAsync);
        app.MapGet("/lots/{id:guid}/book", BookPageAsync).RequireAuthorization("verified");
        app.MapInertia("/about", "about/Policy", new { doc = "about" });
        app.MapInertia("/policies", "about/Policy", new { doc = "policies" });
    }

    static async Task<IResult> HomeAsync(LotsService lots, CancellationToken ct) =>
        Render("Home", new InertiaProps { ["lots"] = await lots.CardsAsync(await lots.ActiveAsync(ct), ct) });

    // q/type/maxPrice/sort carry through the Scroll partial reloads.
    static async Task<IResult> IndexAsync(LotsService lots, string? q, string? type, long? maxPrice, string? sort, int? page, CancellationToken ct) =>
        Render("lots/Index", new InertiaProps
        {
            ["lots"] = Scroll(await lots.QueryPageAsync(q, type, maxPrice, sort, Math.Max(1, page ?? 1), ct), "data"),
            ["filters"] = new { q = q ?? "", type = type ?? "", maxPrice, sort = sort ?? "name" },
        });

    static async Task<IResult> ShowAsync(Guid id, LotsService lots, CancellationToken ct)
    {
        var lot = await lots.FindActiveAsync(id, ct);
        if (lot is null) return HttpExtensions.NotFoundPage();
        var card = (await lots.CardsAsync([lot], ct))[0];
        return Render("lots/Show", new InertiaProps
        {
            ["lot"] = card,
            ["spots"] = await lots.SpotsAsync(id, ct),
            ["tariff"] = LotsMappers.TariffOf(lot),
            ["calendar"] = LotsUtils.Availability(card.Spots, card.Free),
        });
    }

    static async Task<IResult> BookPageAsync(Guid id, string? spot, LotsService lots, CancellationToken ct)
    {
        var lot = await lots.FindActiveAsync(id, ct);
        if (lot is null) return HttpExtensions.NotFoundPage();
        var spots = await lots.SpotsAsync(id, ct);
        return Render("lots/Book", new InertiaProps
        {
            ["lot"] = (await lots.CardsAsync([lot], ct))[0],
            ["tariff"] = LotsMappers.TariffOf(lot),
            ["spots"] = spots.Select(s => new { s.Id, s.Label, s.Level, s.Type, s.State }),
            ["prefillSpot"] = spot,
        });
    }
}
