using System.Net;
using Inertia.Net.Testing;
using CarParker.Features.Lots;

namespace CarParker.Tests;

/// The /lots listing: Mongo text search plus type, price and sort handling.
public sealed class LotListingTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Text_search_matches_name_area_type_and_price_filters()
    {
        var (deck, _) = await app.SeedLotAsync("Harbour Deck Park", ratePerHourCents: 400, spotCount: 2, types: [SpotType.Standard, SpotType.Ev], area: "Harbourside");
        var (mall, _) = await app.SeedLotAsync("Mall Point Parkade", ratePerHourCents: 180, spotCount: 1, area: "Market District");

        async Task<Guid[]> FoundIds(string url)
        {
            using var response = await app.InertiaGetAsync(app.Client(), url);
            var page = await response.GetInertiaPageAsync();
            return page.Prop("lots.data")!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()).ToArray();
        }

        Assert.Equal([deck.Id], await FoundIds("/lots?q=harbour"));    // name match
        Assert.Equal([mall.Id], await FoundIds("/lots?q=market"));     // area match
        Assert.Equal([deck.Id], await FoundIds("/lots?type=ev"));      // spot type filter
        Assert.Equal([mall.Id], await FoundIds("/lots?maxPrice=200")); // hourly rate cap
        Assert.Equal([mall.Id, deck.Id], await FoundIds("/lots?sort=price")); // cheapest first

        // Operators and quotes in user input must not break the text query (no 500).
        using var hostile = await app.Client().GetAsync("/lots?q=%22%20OR%20NEAR(a%20b)%20*");
        Assert.Equal(HttpStatusCode.OK, hostile.StatusCode);
    }

    [Fact]
    public async Task Price_sorted_pages_do_not_repeat_or_load_a_thirteenth_card()
    {
        using var pagingApp = new AppFactory();
        for (var index = 0; index < 13; index++)
            await pagingApp.SeedLotAsync($"Paging {index:D2}", ratePerHourCents: 100 + index, spotCount: 1);

        using var first = await pagingApp.InertiaGetAsync(pagingApp.Client(), "/lots?q=Paging&sort=price");
        var firstPage = await first.GetInertiaPageAsync();
        Assert.Equal(12, firstPage.Prop("lots.data")!.AsArray().Count);
        Assert.True(firstPage.Prop("lots.hasMore")!.GetValue<bool>());

        using var second = await pagingApp.InertiaGetAsync(pagingApp.Client(), "/lots?q=Paging&sort=price&page=2");
        var secondPage = await second.GetInertiaPageAsync();
        Assert.Single(secondPage.Prop("lots.data")!.AsArray());
        Assert.Equal("Paging 12", secondPage.Prop("lots.data")!.AsArray()[0]!["name"]!.GetValue<string>());
        Assert.False(secondPage.Prop("lots.hasMore")!.GetValue<bool>());
    }

    [Fact]
    public async Task Inactive_lots_are_hidden_from_the_listing_and_answer_404()
    {
        var (hidden, _) = await app.SeedLotAsync("Hidden deck", active: false);

        using var listing = await app.InertiaGetAsync(app.Client(), "/lots");
        var page = await listing.GetInertiaPageAsync();
        Assert.DoesNotContain(hidden.Id, page.Prop("lots.data")!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()));

        using var detail = await app.InertiaGetAsync(app.Client(), $"/lots/{hidden.Id}");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        using var missing = await app.InertiaGetAsync(app.Client(), $"/lots/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
