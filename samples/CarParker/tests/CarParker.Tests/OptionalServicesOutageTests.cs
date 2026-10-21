using System.Net;
using Inertia.Net.Testing;
using MongoDB.Driver;
using CarParker.Features.Bookings;
using CarParker.Data;

namespace CarParker.Tests;

/// Meilisearch is deliberately pointed at a dead port; the native engine must answer and core flows must survive.
public sealed class OptionalServicesOutageTests
{
    [Fact]
    public async Task Lot_search_falls_back_to_native_and_core_flows_survive_a_dead_meilisearch()
    {
        await using var app = new AppFactory(meilisearchEnabled: true);
        var (lot, spots) = await app.SeedLotAsync("Harbour Outage Deck", spotCount: 1);

        using var health = await app.Client().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        using var search = await app.InertiaGetAsync(app.Client(), "/lots?q=harbour");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var page = await search.GetInertiaPageAsync();
        Assert.Equal("native", page.Prop("searchEngine")!.GetValue<string>());
        Assert.Contains(lot.Id, page.Prop("lots.data")!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()));

        // Booking writes keep working with the optional search backend down.
        var (client, _) = await app.SeedUserAsync();
        var start = DateTime.UtcNow.AddHours(3);
        using var created = await app.BookAsync(client, lot, spots[0], start, start.AddHours(2));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal(1, await app.QueryAsync(db => db.Reservations.CountDocumentsAsync(r => r.LotId == lot.Id && r.Status == ReservationStatus.Confirmed)));
    }
}
