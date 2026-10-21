using Inertia.Net;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Infrastructure;

namespace SvelteMongoBlog.Features.Lots;

/// <summary>Public lot pages: home, listing with Scroll feed, live spot grid, booking wizard, static docs.
/// Reads its projections from LotsService and owns the Inertia composition for these routes.</summary>
public static class Lots
{
    public static void MapLots(this IEndpointRouteBuilder app)
    {
        app.MapGet("/lots", IndexAsync);
        app.MapGet("/lots/{id:guid}", ShowAsync);
        app.MapGet("/lots/{id:guid}/book", BookPageAsync).RequireAuthorization("verified");
        app.MapGet("/", HomeAsync);
        app.MapGet("/about", () => StaticPage("about"));
        app.MapGet("/policies", () => StaticPage("policies"));
    }

    /// <summary>GET / — hero + lot carousel + how-it-works band (Home.svelte). SSR-visible like the lot pages.</summary>
    static async Task<IResult> HomeAsync(AppDb db, CancellationToken ct)
    {
        var lots = await LotsService.ActiveAsync(db, ct);
        return Render("Home", new InertiaProps { ["lots"] = await LotsService.CardsAsync(db, lots, ct) });
    }

    /// <summary>GET /lots — list page with Scroll feed; q/type/maxPrice/sort carry through partial reloads.</summary>
    static async Task<IResult> IndexAsync(
        AppDb db,
        OptionalServiceNamespace names,
        [FromServices] MeilisearchTaskClient? search,
        IConfiguration config,
        string? q,
        string? type,
        long? maxPrice,
        string? sort,
        int? page,
        CancellationToken ct)
    {
        var pageNo = Math.Max(1, page ?? 1);
        var (pageResult, engine) = await LotsService.QueryPageAsync(db, search, names, config, q, type, maxPrice, sort, pageNo, ct);
        return Render("lots/Index", new InertiaProps
        {
            ["lots"] = Scroll(pageResult, "data"),
            ["filters"] = new { q = q ?? "", type = type ?? "", maxPrice = maxPrice, sort = sort ?? "name" },
            ["searchEngine"] = Always(engine),
        });
    }

    /// <summary>GET /lots/{id} — live spot grid + tariff card. Public and SSR'd.</summary>
    static async Task<IResult> ShowAsync(Guid id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var lot = await db.Lots.Find(l => l.Id == id).FirstOrDefaultAsync(ct);
        if (lot is null || !lot.IsActive)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        return Render("lots/Show", new InertiaProps
        {
            ["lot"] = (await LotsService.CardsAsync(db, [lot], ct))[0],
            ["spots"] = await LotsService.SpotsAsync(db, id, ct),
            ["tariff"] = LotsService.TariffOf(lot),
            ["calendar"] = await LotsService.AvailabilityAsync(db, lot, ct),
        });
    }

    /// <summary>GET /lots/{id}/book — booking wizard (auth+verified): window picker, insurance checkbox, live price.</summary>
    static async Task<IResult> BookPageAsync(Guid id, AppDb db, string? spot, CancellationToken ct)
    {
        var lot = await db.Lots.Find(l => l.Id == id && l.IsActive).FirstOrDefaultAsync(ct);
        if (lot is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        var spots = await db.Spots.Find(s => s.LotId == id && s.Active)
            .SortBy(s => s.Level).ThenBy(s => s.Label)
            .Project(s => new { s.Id, s.Label, s.Level, Type = s.Type.ToString().ToLowerInvariant(), State = s.State.ToString().ToLowerInvariant() })
            .ToListAsync(ct);
        return Render("lots/Book", new InertiaProps
        {
            ["lot"] = (await LotsService.CardsAsync(db, [lot], ct))[0],
            ["tariff"] = LotsService.TariffOf(lot),
            ["spots"] = spots,
            ["prefillSpot"] = spot,
        });
    }

    /// <summary>GET /about and /policies — one static page component driven by the `doc` prop.</summary>
    static IResult StaticPage(string doc) => Render("about/Policy", new InertiaProps { ["doc"] = doc });
}
