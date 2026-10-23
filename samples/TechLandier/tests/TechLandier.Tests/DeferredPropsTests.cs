using System.Text.Json.Nodes;
using Inertia.Net.Testing;
using Microsoft.EntityFrameworkCore;

namespace TechLandier.Tests;

/// Slow secondary widgets leave the first response; their groups arrive on the deferred partial reload.
public sealed class DeferredPropsTests(AppFactory app) : IClassFixture<AppFactory>
{
    static string[] Group(InertiaPage page, string group) =>
        page.Raw["deferredProps"]![group]!.AsArray().Select(p => p!.GetValue<string>()).ToArray();

    [Fact]
    public async Task Account_product_and_buy_pages_defer_their_secondary_widgets()
    {
        var (client, _) = await app.SignUpAsync("deferred@example.test");

        using (var account = await app.InertiaGetAsync(client, "/account"))
        {
            var page = await account.GetInertiaPageAsync();
            Assert.True(page.HasProp("stats"));
            Assert.True(page.HasProp("recentOrders"));
            Assert.False(page.HasProp("nextInstallment"));
            Assert.False(page.HasProp("openDeliveries"));
            Assert.False(page.HasProp("unreadAlerts"));
            Assert.Contains("nextInstallment", Group(page, "installment"));
            Assert.Contains("openDeliveries", Group(page, "deliveries"));
            Assert.Contains("unreadAlerts", Group(page, "alerts"));
        }

        using (var accountPartial = await app.InertiaPartialAsync(client, "/account", "account/Index",
                   "nextInstallment,openDeliveries,unreadAlerts"))
        {
            var page = await accountPartial.GetInertiaPageAsync();
            Assert.True(page.HasProp("nextInstallment"));
            Assert.True(page.HasProp("openDeliveries"));
            Assert.True(page.HasProp("unreadAlerts"));
        }

        var product = await app.SeedProductAsync("Deferred product");
        var slug = await app.QueryAsync(async db => await db.Products.Where(p => p.Id == product.ProductId).Select(p => p.Slug).FirstAsync());

        using (var detail = await app.InertiaGetAsync(client, $"/products/{slug}"))
        {
            var page = await detail.GetInertiaPageAsync();
            Assert.True(page.HasProp("product"));
            Assert.False(page.HasProp("canReview"));
            Assert.Contains("canReview", Group(page, "reviews"));
        }

        using (var detailPartial = await app.InertiaPartialAsync(client, $"/products/{slug}", "products/Show", "reviews,canReview"))
        {
            var page = await detailPartial.GetInertiaPageAsync();
            Assert.True(page.HasProp("canReview"));
        }

        using (var buy = await app.InertiaGetAsync(client, $"/buy/{product.ProductId}"))
        {
            var page = await buy.GetInertiaPageAsync();
            Assert.False(page.HasProp("creditState"));
            Assert.Contains("creditState", Group(page, "credit"));
        }

        using (var buyPartial = await app.InertiaPartialAsync(client, $"/buy/{product.ProductId}", "products/Buy", "creditState"))
        {
            var page = await buyPartial.GetInertiaPageAsync();
            Assert.True(page.HasProp("creditState"));
            Assert.NotNull(page.Prop("creditState.planCount"));
        }
    }
}
