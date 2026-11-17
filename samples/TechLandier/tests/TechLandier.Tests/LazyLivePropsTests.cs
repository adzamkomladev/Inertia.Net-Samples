using TechLandier.Features;
using System.Net;
using Inertia.Net.Testing;
using TechLandier.Data;

namespace TechLandier.Tests;

/// Live props are lazy: a live event reloads only the affected prop (a partial reload), not the whole handler.
public sealed class LazyLivePropsTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Admin_orders_reload_just_the_orders_prop()
    {
        var (admin, _) = await app.AdminAsync("lazy-orders@example.test");

        using (var full = await app.InertiaGetAsync(admin, "/admin/orders"))
        {
            var page = await full.GetInertiaPageAsync();
            Assert.True(page.HasProp("orders"));
            Assert.True(page.HasProp("statusFilter"));
        }

        using var partial = await app.InertiaPartialAsync(admin, "/admin/orders", "admin/Orders", "orders");
        var reload = await partial.GetInertiaPageAsync();
        Assert.True(reload.HasProp("orders"));
        Assert.False(reload.HasProp("statusFilter"));
    }

    [Fact]
    public async Task Admin_order_detail_keeps_its_props_and_reloads_one_at_a_time()
    {
        var (admin, _) = await app.AdminAsync("lazy-order@example.test");
        var (_, ownerId) = await app.SignUpAsync("lazy-order-owner@example.test");
        var number = "TL-LZY-" + Guid.NewGuid().ToString("N")[..8];
        await app.QueryAsync(async db =>
        {
            db.Orders.Add(new Order { Number = number, UserId = ownerId, CheckoutAttemptId = Guid.NewGuid().ToString("N") });
            await db.SaveChangesAsync();
            return 0;
        });

        using (var full = await app.InertiaGetAsync(admin, $"/admin/orders/{number}"))
        {
            var page = await full.GetInertiaPageAsync();
            foreach (var prop in new[] { "order", "events", "delivery", "customer", "payments", "plan", "activeRiders" })
                Assert.True(page.HasProp(prop), prop);
            Assert.Equal(number, page.Prop("order.number")?.ToString());
        }

        using var partial = await app.InertiaPartialAsync(admin, $"/admin/orders/{number}", "admin/Order", "order");
        var reload = await partial.GetInertiaPageAsync();
        Assert.True(reload.HasProp("order"));
        Assert.False(reload.HasProp("events"));
        Assert.False(reload.HasProp("payments"));

        using var missing = await app.InertiaGetAsync(admin, "/admin/orders/TL-DOES-NOT-EXIST");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Delivery_board_pages_history_and_reloads_each_prop_alone()
    {
        var (admin, _) = await app.AdminAsync("lazy-deliveries@example.test");

        using (var full = await app.InertiaGetAsync(admin, "/admin/deliveries"))
        {
            var page = await full.GetInertiaPageAsync();
            foreach (var prop in new[] { "deliveries", "delivered", "deliveredToday", "riders", "zones" })
                Assert.True(page.HasProp(prop), prop);
            Assert.Equal("1", page.Prop("delivered.currentPage")?.ToString());
        }

        using var partial = await app.InertiaPartialAsync(admin, "/admin/deliveries", "admin/Deliveries", "deliveredToday");
        var reload = await partial.GetInertiaPageAsync();
        Assert.True(reload.HasProp("deliveredToday"));
        Assert.False(reload.HasProp("riders"));
        Assert.False(reload.HasProp("deliveries"));
    }

    [Fact]
    public async Task Alerts_inbox_and_unread_count_are_independent_lazy_props()
    {
        var (customer, _) = await app.SignUpAsync("lazy-alerts@example.test");

        using var partial = await app.InertiaPartialAsync(customer, "/account/alerts", "account/Alerts", "unreadCount");
        var reload = await partial.GetInertiaPageAsync();
        Assert.True(reload.HasProp("unreadCount"));
        Assert.False(reload.HasProp("alerts"));

        using var full = await app.InertiaGetAsync(customer, "/account/alerts");
        var page = await full.GetInertiaPageAsync();
        Assert.True(page.HasProp("alerts"));
        Assert.True(page.HasProp("unreadCount"));
    }

    [Fact]
    public async Task Care_desk_pages_the_open_queue()
    {
        var (admin, _) = await app.AdminAsync("lazy-care@example.test");
        foreach (var (tab, prop) in new[] { ("warranties", "warrantyClaims"), ("insurance", "insuranceClaims"), ("repairs", "repairs") })
        {
            using var response = await app.InertiaGetAsync(admin, $"/admin/care?tab={tab}&show=all");
            var page = await response.GetInertiaPageAsync();
            Assert.Equal("1", page.Prop($"{prop}.currentPage")?.ToString());
            Assert.Equal("all", page.Prop("show")?.ToString());
        }
    }

    [Fact]
    public async Task Signed_in_account_pages_encrypt_history()
    {
        var (customer, _) = await app.SignUpAsync("lazy-history@example.test");
        using var account = await app.InertiaGetAsync(customer, "/account/alerts");
        Assert.Equal("true", (await account.GetInertiaPageAsync()).Raw["encryptHistory"]?.ToString());

        var guest = app.Client();
        using var storefront = await app.InertiaGetAsync(guest, "/products");
        Assert.NotEqual("true", (await storefront.GetInertiaPageAsync()).Raw["encryptHistory"]?.ToString());
    }
}
