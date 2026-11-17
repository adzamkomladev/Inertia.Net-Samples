using System.Net;
using Inertia.Net.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechLandier.Data;
using TechLandier.Features;

namespace TechLandier.Tests;

/// AppFactory plus the deterministic demo data, seeded once per fixture.
public sealed class DemoFactory : AppFactory
{
    readonly Lazy<Task> seeded;

    public DemoFactory() => seeded = new(async () =>
    {
        await using var scope = Services.CreateAsyncScope();
        await DemoSeeder.RunAsync(scope.ServiceProvider.GetRequiredService<AppDb>());
    });

    public async Task SeedAsync() => await seeded.Value;
}

/// Loads every page (and its deferred props) against the demo data, so a query that EF cannot translate fails here.
public sealed class PageSmokeTests(DemoFactory app) : IClassFixture<DemoFactory>
{
    async Task Get(HttpClient client, string url, params string[] partial)
    {
        using var response = await app.InertiaGetAsync(client, url);
        Assert.True(response.IsSuccessStatusCode, $"{url} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        if (partial.Length == 0) return;
        var page = await response.GetInertiaPageAsync();
        using var partialResponse = await app.InertiaPartialAsync(client, url, page.Component, string.Join(",", partial));
        Assert.True(partialResponse.IsSuccessStatusCode, $"{url} [{string.Join(",", partial)}] answered {(int)partialResponse.StatusCode}: {await partialResponse.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Storefront_pages_render()
    {
        await app.SeedAsync();
        var guest = app.Client();
        var slug = await app.QueryAsync(async db => await db.Products.Select(p => p.Slug).FirstAsync());
        await Get(guest, "/", "categories", "deals", "arrivals");
        await Get(guest, "/products?q=wireless&sort=hlow&inStock=true&min=10&max=2000");
        await Get(guest, "/products?category=audio&page=2");
        await Get(guest, "/deals");
        await Get(guest, $"/products/{slug}", "reviews", "canReview", "related");
        await Get(guest, "/about");
        await Get(guest, "/support/policy");
    }

    [Fact]
    public async Task Customer_pages_render()
    {
        await app.SeedAsync();
        var (client, uid) = await app.SignUpAsync("smoke-customer@example.test");
        string number;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            // Adopt the demo customer's data so the pages have something to show.
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var demo = await db.Users.Where(u => u.Email == "inertia.demo42.techlandier.customer@yopmail.com").Select(u => u.Id).FirstAsync();
            await db.Orders.Where(o => o.UserId == demo).ExecuteUpdateAsync(s => s.SetProperty(o => o.UserId, uid));
            await db.PayPlans.Where(o => o.UserId == demo).ExecuteUpdateAsync(s => s.SetProperty(o => o.UserId, uid));
            await db.Warranties.Where(o => o.UserId == demo).ExecuteUpdateAsync(s => s.SetProperty(o => o.UserId, uid));
            await db.DeviceInsurances.Where(o => o.UserId == demo).ExecuteUpdateAsync(s => s.SetProperty(o => o.UserId, uid));
            await db.RepairTickets.Where(o => o.UserId == demo).ExecuteUpdateAsync(s => s.SetProperty(o => o.UserId, uid));
            await db.Tickets.Where(o => o.UserId == demo).ExecuteUpdateAsync(s => s.SetProperty(o => o.UserId, uid));
            number = await db.Orders.Where(o => o.UserId == uid).OrderBy(o => o.Number).Select(o => o.Number).FirstAsync();
        }
        await Get(client, "/account", "stats", "recentOrders", "nextInstallment", "openDeliveries", "unreadAlerts");
        await Get(client, "/account/orders");
        await Get(client, $"/account/orders/{number}", "events", "delivery", "plan", "warranties", "insurances", "reviewSlugs");
        await Get(client, "/account/credit");
        await Get(client, "/account/care/warranties");
        await Get(client, "/account/care/insurance");
        await Get(client, "/account/care/repairs");
        var repairId = await app.QueryAsync(async db => await db.RepairTickets.Where(r => r.UserId == uid).Select(r => r.Id).FirstAsync());
        await Get(client, $"/account/care/repairs/{repairId}");
        await Get(client, "/account/support");
        await Get(client, "/account/alerts");
        await Get(client, "/account/profile");
        await Get(client, $"/buy/{await app.QueryAsync(async db => await db.Products.Select(p => p.Id).FirstAsync())}", "creditState");
    }

    [Fact]
    public async Task Admin_pages_render()
    {
        await app.SeedAsync();
        var (admin, _) = await app.AdminAsync("smoke-admin@example.test");
        var number = await app.QueryAsync(async db => await db.Orders.Select(o => o.Number).FirstAsync());
        var ticket = await app.QueryAsync(async db => await db.Tickets.Select(t => t.Number).FirstAsync());
        var productId = await app.QueryAsync(async db => await db.Products.IgnoreQueryFilters().Select(p => p.Id).FirstAsync());
        await Get(admin, "/admin", "kpis", "revenueSeries", "lowStock", "attention");
        await Get(admin, "/admin?range=7", "kpis");
        await Get(admin, "/admin/analytics", "kpis", "series", "topProducts", "categoryShare");
        await Get(admin, "/admin/analytics?range=90", "kpis");
        await Get(admin, "/admin/catalog?q=wire");
        await Get(admin, "/admin/catalog?edit=new");
        await Get(admin, "/admin/catalog?status=low&page=2");
        await Get(admin, "/admin/settings");
        await Get(admin, "/admin/team");
        var plan = await app.QueryAsync(async db => await db.PayPlans.Select(p => p.Id).FirstAsync());
        var warranty = await app.QueryAsync(async db => await db.WarrantyClaims.Select(p => p.Id).FirstAsync());
        var insurance = await app.QueryAsync(async db => await db.InsuranceClaims.Select(p => p.Id).FirstAsync());
        var repair = await app.QueryAsync(async db => await db.RepairTickets.Select(p => p.Id).FirstAsync());
        await Get(admin, $"/admin/credit/{plan}");
        await Get(admin, $"/admin/care/warranty-claims/{warranty}");
        await Get(admin, $"/admin/care/insurance-claims/{insurance}");
        await Get(admin, $"/admin/care/repairs/{repair}");
        using var missing = await app.InertiaGetAsync(admin, $"/admin/care/repairs/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await Get(admin, $"/admin/catalog/{productId}", "detail", "categories", "movements");
        await Get(admin, "/admin/orders?status=paid&q=TL-");
        await Get(admin, $"/admin/orders/{number}", "events", "delivery", "customer", "payments", "plan", "activeRiders");
        await Get(admin, "/admin/deliveries");
        await Get(admin, "/admin/credit?status=overdue");
        await Get(admin, "/admin/credit");
        await Get(admin, "/admin/support?filter=overdue");
        await Get(admin, "/admin/support?filter=mine");
        await Get(admin, $"/admin/support/{ticket}");
        await Get(admin, "/admin/care");
        await Get(admin, "/admin/care?tab=insurance");
        await Get(admin, "/admin/care?tab=repairs");
        await Get(admin, "/admin/reviews?state=hidden");
    }

    [Fact]
    public async Task Cron_tasks_run_against_demo_data()
    {
        await app.SeedAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tasks = scope.ServiceProvider.GetRequiredService<TechLandier.Infrastructure.CronTasks>();
        var ct = CancellationToken.None;
        await tasks.SalesRollupAsync(ct);
        await tasks.BnplRemindersAsync(ct);
        await tasks.BnplOverdueAsync(ct);
        await tasks.BnplStatementAsync(ct);
        await tasks.TicketEscalationAsync(ct);
        await tasks.OrderTimeoutAsync(ct);
        await tasks.OrphanAssetsAsync(ct);
        await tasks.PruneViewsAsync(ct);
        await tasks.PruneUnverifiedAsync(ct);
        Assert.True(await scope.ServiceProvider.GetRequiredService<AppDb>().SalesDailyStats.AnyAsync());
    }
}

public sealed class SearchTests(AppFactory app) : IClassFixture<AppFactory>
{
    async Task<string[]> Search(string q)
    {
        using var response = await app.InertiaGetAsync(app.Client(), $"/products?q={Uri.EscapeDataString(q)}");
        var page = await response.GetInertiaPageAsync();
        return [.. page.Prop("products")!["data"]!.AsArray().Select(p => p!["name"]!.GetValue<string>())];
    }

    [Fact]
    public async Task Search_matches_words_substrings_and_typos()
    {
        await app.SeedProductAsync("Zorblax Wireless Headphones");
        await app.SeedProductAsync("Zorblax Desk Lamp");

        Assert.Contains("Zorblax Wireless Headphones", await Search("headphones"));
        Assert.DoesNotContain("Zorblax Desk Lamp", await Search("headphones"));
        Assert.Contains("Zorblax Wireless Headphones", await Search("zorbl"));
        Assert.Contains("Zorblax Wireless Headphones", await Search("zorblax headfones"));
        Assert.Empty(await Search("qwertyuiop"));
    }

    [Fact]
    public async Task Search_treats_like_wildcards_literally()
    {
        await app.SeedProductAsync("Percent Sign Speaker");
        Assert.Empty(await Search("%%%%"));
    }
}

public sealed class FlowTests(AppFactory app) : IClassFixture<AppFactory>
{
    static object Checkout(AppFactory.SeededProduct p, string attempt, string method = "card", int qty = 1, int? bnpl = null, bool insurance = false) => new
    {
        productId = p.ProductId, qty, zoneId = p.ZoneId, paymentMethod = method, recipientName = "Ama Mensah", phone = "+233 24 000 0001",
        streetLabel = "GA-183-5287 Nyaneba Avenue", insurance, bnplCount = bnpl, checkoutAttemptId = attempt,
    };

    async Task<Order> OrderAsync(Guid userId) => await app.QueryAsync(async db => await db.Orders.AsNoTracking().Include(o => o.Items).OrderByDescending(o => o.CreatedAt).FirstAsync(o => o.UserId == userId));

    [Fact]
    public async Task Checkout_reserves_stock_creates_care_and_plan_and_is_idempotent()
    {
        var paidMailsBefore = await app.RunwickJobsAsync("mail.order-paid");
        var p = await app.SeedProductAsync("Flow Phone", price: 100m, discountPercent: 10, stock: 5, warrantyMonths: 12, insurable: true);
        var (client, uid) = await app.SignUpAsync("buyer@example.test");
        var attempt = Guid.NewGuid().ToString();
        using var first = await app.SendAsync(client, "/checkout", Checkout(p, attempt, qty: 2, bnpl: 3, insurance: true));
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        using (await app.SendAsync(client, "/checkout", Checkout(p, attempt, qty: 2, bnpl: 3, insurance: true))) { }

        var order = await OrderAsync(uid);
        Assert.Equal(197.5m, order.Total); // 2 x 90.00 + 4.00 shipping + 13.50 insurance premium (15% of the 90.00 paid per unit)
        var policy = await app.QueryAsync(async db => await db.DeviceInsurances.AsNoTracking().SingleAsync(d => d.UserId == uid));
        Assert.Equal(13.5m, policy.Premium);
        Assert.Equal(81m, policy.CoverageAmount); // 90% of the price paid, not of the 100.00 list price
        Assert.Equal(OrderStatuses.Paid, order.Status); // Development settles card payments instantly
        Assert.Equal(1, await app.QueryAsync(async db => await db.Orders.CountAsync(o => o.UserId == uid)));
        Assert.Equal(3, await app.QueryAsync(async db => await db.Products.IgnoreQueryFilters().Where(x => x.Id == p.ProductId).Select(x => x.Stock).SingleAsync()));
        Assert.Equal(1, await app.QueryAsync(async db => await db.Warranties.CountAsync(w => w.UserId == uid)));
        Assert.Equal(1, await app.QueryAsync(async db => await db.DeviceInsurances.CountAsync(d => d.UserId == uid)));
        Assert.Equal(3, await app.QueryAsync(async db => await db.Installments.CountAsync(i => i.Plan.UserId == uid)));
        // The retried checkout stages nothing: one receipt job for the one order, committed with it.
        Assert.Equal(paidMailsBefore + 1, await app.RunwickJobsAsync("mail.order-paid"));
    }

    [Fact]
    public async Task Plan_orders_expose_their_plan_and_signed_in_owners_leave_the_tracking_link()
    {
        var p = await app.SeedProductAsync("Plan Brief Phone", price: 100m, stock: 5);
        var (client, uid) = await app.SignUpAsync("plan-brief@example.test");
        using (await app.SendAsync(client, "/checkout", Checkout(p, "pb1", bnpl: 6))) { }
        var order = await OrderAsync(uid);
        using (await app.SendAsync(client, "/checkout", Checkout(p, "pb2"))) { }

        using var account = await app.InertiaGetAsync(client, $"/account/orders/{order.Number}");
        await account.AssertInertiaAsync(page => page.Component("account/Order").Where("order.plan.count", 6).Where("order.plan.paid", 0).Etc());
        using var list = await app.InertiaGetAsync(client, "/account/orders");
        await list.AssertInertiaAsync(page => page.Component("account/Orders").Where("orders.1.plan.count", 6).Etc());
        var noPlan = await OrderAsync(uid);
        using var plainOrder = await app.InertiaGetAsync(client, $"/account/orders/{noPlan.Number}");
        await plainOrder.AssertInertiaAsync(page => page.Component("account/Order").Missing("order.plan.count").Etc());

        // Guests keep the tracking page, with the plan; a verified owner is sent to the account page.
        using var guest = await app.InertiaGetAsync(app.Client(), $"/orders/{order.Number}?t={Uri.EscapeDataString(order.AccessToken)}");
        await guest.AssertInertiaAsync(page => page.Component("orders/Track").Where("order.plan.count", 6).Etc());
        using var owner = await client.GetAsync($"/orders/{order.Number}?t={Uri.EscapeDataString(order.AccessToken)}");
        Assert.Equal($"/account/orders/{order.Number}", owner.RequestMessage?.RequestUri?.AbsolutePath); // the client follows the redirect
    }

    [Fact]
    public async Task Discounted_products_strike_the_list_price_the_percent_and_the_saving_agree()
    {
        await app.SeedProductAsync("Consistent Price Phone", price: 200m, discountPercent: 25, stock: 5);
        using var response = await app.InertiaGetAsync(app.Client(), "/products?q=Consistent+Price+Phone");
        await response.AssertInertiaAsync(page => page.Where("products.data.0.price", 200m).Where("products.data.0.compareAtPrice", 200m)
            .Where("products.data.0.discountPercent", 25).Where("products.data.0.displayPrice", 150m).Etc());
    }

    [Fact]
    public async Task Order_timeout_cancels_stale_orders_releases_stock_and_stages_the_cancel_email()
    {
        var p = await app.SeedProductAsync("Timeout Tablet", price: 50m, stock: 4);
        var (client, uid) = await app.SignUpAsync("timeout@example.test");
        using (await app.SendAsync(client, "/checkout", Checkout(p, "t1", method: "mobile_money", qty: 2))) { }
        var order = await OrderAsync(uid);
        Assert.Equal(2, await app.QueryAsync(async db => await db.Products.IgnoreQueryFilters().Where(x => x.Id == p.ProductId).Select(x => x.Stock).SingleAsync()));
        await app.QueryAsync(async db => await db.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatuses.Pending)
            .SetProperty(o => o.PaymentStatus, PaymentStatuses.Unpaid).SetProperty(o => o.CreatedAt, DateTime.UtcNow.AddHours(-2))));
        // Development settles card and mobile money at once; rewind the order to an unpaid one that timed out.
        var mailsBefore = await app.RunwickJobsAsync("mail.order-cancelled");

        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TechLandier.Infrastructure.CronTasks>().OrderTimeoutAsync(CancellationToken.None);

        Assert.Equal(OrderStatuses.Cancelled, (await OrderAsync(uid)).Status);
        Assert.Equal(4, await app.QueryAsync(async db => await db.Products.IgnoreQueryFilters().Where(x => x.Id == p.ProductId).Select(x => x.Stock).SingleAsync()));
        Assert.Equal(mailsBefore + 1, await app.RunwickJobsAsync("mail.order-cancelled"));
    }
    [Fact]
    public async Task Delivery_dispatch_records_attempts_notes_handover_and_rider_capacity()
    {
        var p = await app.SeedProductAsync("Dispatch Speaker", price: 40m, stock: 3);
        var (admin, _) = await app.AdminAsync("dispatch-admin@example.test");
        var (client, uid) = await app.SignUpAsync("dispatch@example.test");

        using (await app.SendAsync(client, "/checkout", Checkout(p, "d1"))) { }
        var order = await OrderAsync(uid);
        foreach (var to in new[] { "processing", "shipped" })
            using (await app.SendAsync(admin, $"/admin/orders/{order.Number}/status", new { to })) { }
        var id = await app.QueryAsync(async db => await db.Deliveries.Where(d => d.OrderId == order.Id).Select(d => d.Id).SingleAsync());
        async Task<Delivery> Load() => await app.QueryAsync(async db => await db.Deliveries.SingleAsync(d => d.Id == id));

        using (await app.SendAsync(admin, $"/admin/deliveries/{id}/note", new { note = new string('x', 201) })) { }
        Assert.Null((await Load()).Note);
        using (await app.SendAsync(admin, $"/admin/deliveries/{id}/note", new { note = "  Call on arrival  " })) { }
        Assert.Equal("Call on arrival", (await Load()).Note);

        using (await app.SendAsync(admin, $"/admin/deliveries/{id}/assign", new { riderId = p.RiderId.ToString() })) { }
        using (await app.SendAsync(admin, $"/admin/deliveries/{id}/fail", new { reason = "Nobody at the address" })) { }
        using (await app.SendAsync(admin, $"/admin/deliveries/{id}/fail", new { reason = "Repeated click" })) { }
        var failed = await Load();
        Assert.Equal((DeliveryStatuses.Failed, 1, "Nobody at the address"), (failed.Status, failed.Attempts, failed.FailedReason));

        using (await app.SendAsync(admin, $"/admin/deliveries/{id}/assign", new { riderId = p.RiderId.ToString() })) { }
        using (await app.SendAsync(admin, $"/admin/deliveries/{id}/pickup", new { })) { }
        using (await app.SendAsync(admin, $"/admin/deliveries/{id}/deliver", new { receivedBy = new string('x', 81) })) { }
        Assert.Equal(DeliveryStatuses.PickedUp, (await Load()).Status);
        using (await app.SendAsync(admin, $"/admin/deliveries/{id}/deliver", new { receivedBy = " Front desk " })) { }
        var done = await Load();
        Assert.Equal((DeliveryStatuses.Delivered, 1, "Front desk"), (done.Status, done.Attempts, done.ReceivedBy));

        using (await app.SendAsync(admin, $"/admin/riders/{p.RiderId}", new { capacity = 0 })) { }
        Assert.Equal(RiderCapacity.Default, await app.QueryAsync(async db => await db.Riders.Where(r => r.Id == p.RiderId).Select(r => r.Capacity).SingleAsync()));
        using (await app.SendAsync(admin, $"/admin/riders/{p.RiderId}", new { capacity = 12 })) { }
        Assert.Equal(12, await app.QueryAsync(async db => await db.Riders.Where(r => r.Id == p.RiderId).Select(r => r.Capacity).SingleAsync()));
    }

    [Fact]
    public async Task Order_lifecycle_through_delivery_review_and_refund()
    {
        var p = await app.SeedProductAsync("Lifecycle Watch", price: 50m, stock: 4);
        var (admin, _) = await app.AdminAsync("lifecycle-admin@example.test");
        var (client, uid) = await app.SignUpAsync("lifecycle@example.test");

        using (await app.SendAsync(client, "/checkout", Checkout(p, "a1"))) { }
        var order = await OrderAsync(uid);
        foreach (var to in new[] { "processing", "shipped" })
            using (await app.SendAsync(admin, $"/admin/orders/{order.Number}/status", new { to })) { }
        var delivery = await app.QueryAsync(async db => await db.Deliveries.SingleAsync(d => d.OrderId == order.Id));
        using (await app.SendAsync(admin, $"/admin/deliveries/{delivery.Id}/assign", new { riderId = p.RiderId.ToString() })) { }
        using (await app.SendAsync(admin, $"/admin/deliveries/{delivery.Id}/pickup", new { })) { }
        using (await app.SendAsync(admin, $"/admin/deliveries/{delivery.Id}/deliver", new { })) { }
        Assert.Equal(OrderStatuses.Delivered, (await OrderAsync(uid)).Status);

        using (await app.SendAsync(client, "/account/reviews", new { productId = p.ProductId, rating = 4, title = "Fine", body = "Works well." })) { }
        Assert.Equal(1, await app.QueryAsync(async db => await db.Products.IgnoreQueryFilters().Where(x => x.Id == p.ProductId).Select(x => x.RatingCount).SingleAsync()));
        var review = await app.QueryAsync(async db => await db.Reviews.SingleAsync(r => r.UserId == uid));
        using (await app.SendAsync(admin, $"/admin/reviews/{review.Id}", new { state = "hidden" })) { }
        Assert.Equal(0, await app.QueryAsync(async db => await db.Products.IgnoreQueryFilters().Where(x => x.Id == p.ProductId).Select(x => x.RatingCount).SingleAsync()));
        using (await app.SendAsync(admin, $"/admin/reviews/{review.Id}", new { state = "published" })) { }
        Assert.Equal(1, await app.QueryAsync(async db => await db.Products.IgnoreQueryFilters().Where(x => x.Id == p.ProductId).Select(x => x.RatingCount).SingleAsync()));

        // A second, paid order gets refunded: stock comes back.
        using (await app.SendAsync(client, "/checkout", Checkout(p, "a2", qty: 2))) { }
        var second = await OrderAsync(uid);
        Assert.Equal(1, await app.QueryAsync(async db => await db.Products.IgnoreQueryFilters().Where(x => x.Id == p.ProductId).Select(x => x.Stock).SingleAsync()));
        using (await app.SendAsync(admin, $"/admin/orders/{second.Number}/refund", new { note = "changed mind" })) { }
        Assert.Equal(PaymentStatuses.Refunded, (await OrderAsync(uid)).PaymentStatus);
        Assert.Equal(3, await app.QueryAsync(async db => await db.Products.IgnoreQueryFilters().Where(x => x.Id == p.ProductId).Select(x => x.Stock).SingleAsync()));
    }

    [Fact]
    public async Task Support_threads_between_customer_and_admin()
    {
        var (admin, _) = await app.AdminAsync("support-admin@example.test");
        var (client, uid) = await app.SignUpAsync("support@example.test");
        using (await app.SendAsync(client, "/account/support", new { subject = "Broken screen", category = "product_fault", message = "It cracked on day one." })) { }
        var ticket = await app.QueryAsync(async db => await db.Tickets.SingleAsync(t => t.UserId == uid));
        Assert.StartsWith("T-", ticket.Number);

        using (await app.SendAsync(admin, $"/admin/support/{ticket.Id}/reply", new { message = "Sorry, sending a replacement." })) { }
        using (await app.SendAsync(admin, $"/admin/support/{ticket.Id}", new { action = "resolve", note = "Replacement shipped" })) { }
        using (await app.SendAsync(client, $"/account/support/{ticket.Id}/reply", new { message = "Thanks, but it is still broken." })) { }

        var after = await app.QueryAsync(async db => await db.Tickets.SingleAsync(t => t.Id == ticket.Id));
        Assert.Equal(TicketStatuses.Open, after.Status);
        Assert.NotNull(after.FirstResponseAt);
        Assert.Equal(4, await app.QueryAsync(async db => await db.TicketMessages.CountAsync(m => m.TicketId == ticket.Id)));

        // A staff reply after resolving reopens the ticket with a fresh response window.
        using (await app.SendAsync(admin, $"/admin/support/{ticket.Id}", new { action = "resolve", note = "Fixed" })) { }
        using (await app.SendAsync(admin, $"/admin/support/{ticket.Id}/reply", new { message = "One more thing to check." })) { }
        var reopened = await app.QueryAsync(async db => await db.Tickets.SingleAsync(t => t.Id == ticket.Id));
        Assert.Equal((TicketStatuses.Open, (DateTime?)null), (reopened.Status, reopened.ResolvedAt));
        Assert.True(reopened.DueAt > DateTime.UtcNow);

        using (await app.SendAsync(app.Client(), "/support", new { name = "Kofi Guest", email = "guest@example.test", subject = "Late parcel", category = "late_delivery", message = "Where is it?" })) { }
        Assert.Equal("guest@example.test", await app.QueryAsync(async db => await db.Tickets.Where(t => t.Subject == "Late parcel").Select(t => t.GuestEmail).SingleAsync()));
    }

    [Fact]
    public async Task Admin_catalog_create_edit_restock_and_search()
    {
        var p = await app.SeedProductAsync("Catalog Anchor");
        var (admin, _) = await app.AdminAsync("catalog-admin@example.test");
        var specs = """[{"label":"Ports","value":"USB-C"},{"label":"","value":"skipped"}]""";
        using (await app.SendAsync(admin, "/admin/catalog", new
        {
            name = "Quokka Charger", brand = "Quokka", categoryId = p.CategoryId, price = 25m, stock = 3, specs, status = "active", keywords = "powerbank",
        })) { }
        var product = await app.QueryAsync(async db => await db.Products.SingleAsync(x => x.Name == "Quokka Charger"));
        Assert.Equal("quokka-charger", product.Slug);
        Assert.Contains("USB-C", product.SpecsJson); // jsonb reformats the text, so compare content only
        Assert.DoesNotContain("skipped", product.SpecsJson);

        using (await app.SendAsync(admin, $"/admin/catalog/{product.Id}", new { name = "Quokka Charger", brand = "Quokka", categoryId = p.CategoryId, price = 30m, stock = 10, discountPercent = 10 })) { }
        using (await app.SendAsync(admin, $"/admin/catalog/{product.Id}/restock", new { delta = -4, note = "damaged" })) { }
        var edited = await app.QueryAsync(async db => await db.Products.SingleAsync(x => x.Id == product.Id));
        Assert.Equal(6, edited.Stock);
        Assert.Equal(30m, edited.Price);
        Assert.Equal(3, await app.QueryAsync(async db => await db.StockMovements.CountAsync(m => m.ProductId == product.Id)));

        using var search = await app.InertiaGetAsync(app.Client(), "/products?q=powerbank");
        await search.AssertInertiaAsync(page => page.Has("products.data").Etc());
        Assert.Contains("Quokka Charger", await search.Content.ReadAsStringAsync());
    }
}
