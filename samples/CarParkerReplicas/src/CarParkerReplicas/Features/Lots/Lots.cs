using System.Security.Claims;
using CarParker.Features.Bookings;
using CarParker.Infrastructure;

namespace CarParker.Features.Lots;

public static class Lots
{
    public static void MapLots(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", HomeAsync).WithName("home");
        app.MapGet("/lots", IndexAsync).WithName("lots.index");
        app.MapGet("/lots/{id:guid}", ShowAsync).WithName("lots.show");
        app.MapGet("/lots/{id:guid}/book", BookPageAsync).RequireAuthorization("verified").WithName("lots.book");
        app.MapGet("/about", () => Render(new AboutPage("about"))).WithName("pages.about");
        app.MapGet("/policies", () => Render(new AboutPage("policies"))).WithName("pages.policies");
    }

    static async Task<IResult> HomeAsync(LotsService lots, CancellationToken ct) =>
        Render(new HomePage(await lots.CardsAsync(await lots.ActiveAsync(ct), ct)));

    // q/type/maxPrice/sort carry through the Scroll partial reloads.
    static async Task<IResult> IndexAsync(LotsService lots, string? q, string? type, long? maxPrice, string? sort, PageRequest page, CancellationToken ct) =>
        await RenderLotsIndexAsync(lots, ct, q, type, maxPrice, sort, page);

    /// <summary>The lots page render shared by GET /lots and the login POST, which renders it inline after signing in.</summary>
    internal static async Task<IResult> RenderLotsIndexAsync(LotsService lots, CancellationToken ct,
        string? q = null, string? type = null, long? maxPrice = null, string? sort = null, PageRequest? page = null) =>
        Render(new LotsIndexPage(
            Scroll(await lots.QueryPageAsync(q, type, maxPrice, sort, (page ?? new PageRequest()) with { PerPage = LotsService.PageSize }, ct), "data"),
            new LotsFilter(q ?? "", type ?? "", maxPrice, sort ?? "name")));

    static async Task<IResult> ShowAsync(Guid id, LotsService lots, CancellationToken ct)
    {
        var lotTask = lots.FindActiveAsync(id, ct);
        var spotsTask = lots.SpotsAsync(id, ct);
        await Task.WhenAll(lotTask, spotsTask);
        var lot = await lotTask;
        if (lot is null) return HttpExtensions.NotFoundPage();
        var spots = await spotsTask;
        var card = LotsService.CardFromSpots(lot, spots);
        return Render(new LotShowPage(card, spots, LotsMappers.TariffOf(lot), LotsUtils.Availability(card.Spots, card.Free, lot.TimeZoneId)));
    }

    /// <summary>The booking page. The window query (the same lot-local fields the booking POST takes) turns the bay map
    /// into availability for that window; the page sends it on a debounced partial reload of `spots` + `spotsWindow`.</summary>
    static async Task<IResult> BookPageAsync(Guid id, string? spot, string? windowType, string? date, string? timeFrom, string? timeTo,
        string? daysOfWeek, int? weeks, LotsService lots, BookingsService bookings, PreferencesService preferences, ClaimsPrincipal user, CancellationToken ct)
    {
        var lotTask = lots.FindActiveAsync(id, ct);
        var spotsTask = lots.SpotsAsync(id, ct);
        var vehiclesTask = preferences.VehiclesAsync(user.UserId(), ct);
        await Task.WhenAll(lotTask, spotsTask, vehiclesTask);
        var lot = await lotTask;
        if (lot is null) return HttpExtensions.NotFoundPage();
        var spots = await spotsTask;
        var window = new BookingInput
        {
            LotId = id, WindowType = windowType, Date = date, TimeFrom = timeFrom, TimeTo = timeTo, DaysOfWeek = daysOfWeek, Weeks = weeks ?? 1,
        };
        var forWindow = windowType is null ? null : await bookings.SpotsForWindowAsync(lot, spots, window, ct);
        return Render("lots/Book", new InertiaProps
        {
            ["lot"] = LotsService.CardFromSpots(lot, spots),
            ["tariff"] = LotsMappers.TariffOf(lot),
            ["spots"] = (forWindow ?? spots).Select(s => new { s.Id, s.Label, s.Level, s.Type, s.State }),
            // Echo of the window the map reflects, null for "right now". BookingFlow builds the same key from its query.
            ["spotsWindow"] = forWindow is null ? null
                : windowType == "recurring" ? string.Join('|', windowType, timeFrom, timeTo, daysOfWeek, weeks)
                : string.Join('|', windowType, date, timeFrom, timeTo),
            ["prefillSpot"] = spot,
            ["vehicles"] = await vehiclesTask,
        });
    }
}
