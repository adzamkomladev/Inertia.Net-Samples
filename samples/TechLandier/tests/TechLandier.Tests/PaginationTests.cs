using System.Text.Json.Nodes;
using Inertia.Net.Testing;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Features;

namespace TechLandier.Tests;

/// Numbered pages come from ToPageAsync (Inertia.Net.EntityFrameworkCore) and routes bind PageRequest:
/// the payload keeps data/currentPage/perPage/total/lastPage/from/to/hasMore and Scroll metadata follows it.
public sealed class PaginationTests(DemoFactory app) : IClassFixture<DemoFactory>
{
    static JsonObject PagedJson(InertiaPage page, string prop) => page.Prop(prop)!.AsObject();

    [Fact]
    public async Task Storefront_products_pages_carry_the_numbered_shape_and_scroll_metadata()
    {
        await app.SeedAsync();
        var guest = app.Client();

        using var firstResponse = await app.InertiaGetAsync(guest, "/products");
        var first = await firstResponse.GetInertiaPageAsync();
        // The storefront rails load with one page of twelve and metadata that the InfiniteScroll uses.
        new AssertableInertia(first).HasScroll("products", 1, 2).Has("products.data", 12);
        var firstPage = PagedJson(first, "products");
        Assert.Equal(1, (int)firstPage["currentPage"]!);
        Assert.Equal(12, (int)firstPage["perPage"]!);
        Assert.Equal(1, (int)firstPage["from"]!);
        Assert.Equal(12, (int)firstPage["to"]!);
        Assert.True((bool)firstPage["hasMore"]!);
        Assert.Equal("page", (string)firstPage["pageName"]!);

        // Next page keeps every other prop and replaces only the page parameter (like the client's InfiniteScroll).
        var second = await first.LoadNextPageAsync(guest, "products");
        Assert.Equal("/products?page=2", second.Url);
        new AssertableInertia(second).HasScroll("products", 2, 3).Has("products.data", 12);
        var secondPage = PagedJson(second, "products");
        Assert.Equal(2, (int)secondPage["currentPage"]!);
        Assert.Equal(13, (int)secondPage["from"]!);
        Assert.Equal(24, (int)secondPage["to"]!);
        // The demo store seeds 120 products, two of them drafts, so a counted page knows its own size.
        Assert.InRange((int)secondPage["total"]!, 100, int.MaxValue);
        Assert.True((bool)secondPage["hasMore"]!);
    }

    [Fact]
    public async Task Storefront_counted_pages_show_totals_and_last_page()
    {
        await app.SeedAsync();
        var guest = app.Client();
        var response = await app.InertiaGetAsync(guest, "/deals?page=2");
        var page = await response.GetInertiaPageAsync();
        var deals = PagedJson(page, "products");
        Assert.Equal(2, (int)deals["currentPage"]!);
        Assert.Equal(12, (int)deals["perPage"]!);
        Assert.InRange((int)deals["total"]!, 0, int.MaxValue);
        var expectedLast = ((long)deals["total"]! + 11) / 12;
        Assert.Equal(expectedLast, (long)deals["lastPage"]!);
    }

    [Fact]
    public async Task Admin_catalog_pager_pages_with_totals()
    {
        await app.SeedAsync();
        var (admin, _) = await app.AdminAsync("pager-admin@example.test");
        var response = await app.InertiaGetAsync(admin, "/admin/catalog?page=2");
        var page = await response.GetInertiaPageAsync();
        var products = PagedJson(page, "products");
        // The console catalog includes every status, 20 rows per page.
        Assert.Equal(2, (int)products["currentPage"]!);
        Assert.Equal(20, (int)products["perPage"]!);
        Assert.Equal(21, (int)products["from"]!);
        Assert.Equal(40, (int)products["to"]!);
        Assert.Equal(120, (int)products["total"]!);
        Assert.Equal(6, (int)products["lastPage"]!);
        Assert.True((bool)products["hasMore"]!);
    }

    [Fact]
    public async Task Admin_orders_reload_pages_forward_without_a_count()
    {
        await app.SeedAsync();
        var (admin, _) = await app.AdminAsync("orders-pager@example.test");
        var response = await app.InertiaGetAsync(admin, "/admin/orders?page=2");
        var page = await response.GetInertiaPageAsync();
        var orders = PagedJson(page, "orders");
        // The live console list skips the total count (countTotal: false) and pages forward from hasMore.
        Assert.Equal(2, (int)orders["currentPage"]!);
        Assert.Equal(25, (int)orders["perPage"]!);
        Assert.True((bool)orders["hasMore"]!);
        Assert.Null(orders["total"]);
        Assert.Null(orders["lastPage"]);

        // SignalR pulse reloads fetch just the orders prop with the same shape.
        using var partial = await app.InertiaPartialAsync(admin, "/admin/orders?page=2", "admin/Orders", "orders");
        var reload = await partial.GetInertiaPageAsync();
        var reloaded = PagedJson(reload, "orders");
        Assert.Equal(2, (int)reloaded["currentPage"]!);
        Assert.True((bool)reloaded["hasMore"]!);
    }

    [Fact]
    public async Task Account_alerts_inbox_pages_forward()
    {
        await app.SeedAsync();
        var (client, uid) = await app.SignUpAsync("alerts-pager@example.test");
        // Two full pages plus extra, so page two holds 25 rows and still has more after it.
        await app.QueryAsync(async db =>
        {
            var now = DateTime.UtcNow;
            for (var i = 0; i < 55; i++)
                db.Alerts.Add(new Alert
                {
                    UserId = uid, Kind = AlertKinds.OrderStatus, Title = $"Alert {i}",
                    Body = "Seeded for pagination", CreatedAt = now.AddMinutes(-i),
                });
            return await db.SaveChangesAsync();
        });

        var response = await app.InertiaGetAsync(client, "/account/alerts?page=2");
        var page = await response.GetInertiaPageAsync();
        var alerts = PagedJson(page, "alerts");
        Assert.Equal(2, (int)alerts["currentPage"]!);
        Assert.Equal(25, (int)alerts["perPage"]!);
        Assert.NotNull(alerts["data"]!.AsArray().First());
        Assert.True((bool)alerts["hasMore"]!);
        // The inbox only counts unread separately, no total (countTotal: false).
        Assert.Null(alerts["total"]);
    }
}
