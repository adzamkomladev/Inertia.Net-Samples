using System.Net;
using Inertia.Net.Testing;

namespace CarParker.Tests;

/// The /account dashboard is assembled from parallel lookups; its projection must stay unchanged.
public sealed class AccountDashboardTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Account_dashboard_projects_the_booking_and_lot_data()
    {
        var (client, _) = await app.SeedUserAsync();
        var (lot, spots) = await app.SeedLotAsync("Dashboard deck", spotCount: 2);
        var start = DateTime.UtcNow.AddHours(2);

        using var created = await app.BookAsync(client, lot, spots[1], start, start.AddHours(2));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        using var response = await app.InertiaGetAsync(client, "/account");
        var page = await response.GetInertiaPageAsync();

        Assert.Equal(1, page.Prop("account.upcomingCount")!.GetValue<int>());
        Assert.Single(page.Prop("account.upcoming")!.AsArray());
        Assert.Equal(lot.Name, page.Prop("account.upcoming.0.lotName")!.GetValue<string>());
        Assert.Equal(spots[1].Label, page.Prop("account.upcoming.0.spotLabel")!.GetValue<string>());
        Assert.Equal(lot.Name, page.Prop("account.nextVisit.lotName")!.GetValue<string>());
        Assert.Equal(spots[1].Label, page.Prop("account.nextVisit.spotLabel")!.GetValue<string>());
        Assert.Null(page.Prop("account.activeSession"));
        Assert.Equal(0, page.Prop("account.unreadAlerts")!.GetValue<int>());
        Assert.Contains(page.Prop("lots")!.AsArray(), l => l!["id"]!.GetValue<Guid>() == lot.Id);
    }
}
