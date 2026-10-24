using Inertia.Net;
using Inertia.Net.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechLandier.Features;
using TechLandier.Infrastructure;

namespace TechLandier.Tests;

/// The shared notification summary is cached per user and every write path refreshes it.
public sealed class NotificationCacheTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Shared_summary_is_cached_per_user_and_invalidated_by_writes()
    {
        var (customer, uid) = await app.SignUpAsync("cache-customer@example.test");
        var (admin, _) = await app.AdminAsync("cache-admin@example.test");
        await using var scope = app.Services.CreateAsyncScope();
        var care = scope.ServiceProvider.GetRequiredService<CareService>();
        var alerts = scope.ServiceProvider.GetRequiredService<AlertsService>();
        var realtime = scope.ServiceProvider.GetRequiredService<RealtimeEvents>();

        using (var initial = await app.InertiaGetAsync(customer, "/account"))
        {
            var page = await initial.GetInertiaPageAsync();
            Assert.Equal(0, page.Prop("notifications.unreadCount")!.GetValue<int>());
        }

        Assert.Equal("success", (await care.CreateRepairAsync(uid, new RepairInput("Phone", null, "Screen cracked", null), default)).FlashType);
        using (var afterFirst = await app.InertiaGetAsync(customer, "/account"))
        {
            var page = await afterFirst.GetInertiaPageAsync();
            Assert.Equal(1, page.Prop("notifications.unreadCount")!.GetValue<int>());
            Assert.Single(page.Prop("notifications.items")!.AsArray());
        }

        Assert.Equal("success", (await care.CreateRepairAsync(uid, new RepairInput("Tablet", null, "Battery drains", null), default)).FlashType);
        using (var afterSecond = await app.InertiaGetAsync(customer, "/account"))
        {
            var page = await afterSecond.GetInertiaPageAsync();
            Assert.Equal(2, page.Prop("notifications.unreadCount")!.GetValue<int>());
            Assert.Equal(2, page.Prop("notifications.items")!.AsArray().Count);
        }

        var alertId = await app.QueryAsync(async db =>
            await db.Alerts.Where(a => a.UserId == uid && a.ReadAt == null).Select(a => a.Id).FirstAsync());
        await alerts.MarkReadAsync(uid, alertId, default);
        using (var afterReadOne = await app.InertiaGetAsync(customer, "/account"))
        {
            var page = await afterReadOne.GetInertiaPageAsync();
            Assert.Equal(1, page.Prop("notifications.unreadCount")!.GetValue<int>());
        }

        // The cache key is per user: the admin's summary never shows the customer's alerts.
        using (var adminBefore = await app.InertiaGetAsync(admin, "/account"))
        {
            var page = await adminBefore.GetInertiaPageAsync();
            Assert.Equal(0, page.Prop("notifications.unreadCount")!.GetValue<int>());
        }

        await realtime.AdminPulseAsync("Admin probe", "An admin-only notice.", "/admin");
        using (var adminAfter = await app.InertiaGetAsync(admin, "/account"))
        {
            var page = await adminAfter.GetInertiaPageAsync();
            Assert.Equal(1, page.Prop("notifications.unreadCount")!.GetValue<int>());
        }

        await alerts.MarkAllReadAsync(uid, default);
        using (var afterRead = await app.InertiaGetAsync(customer, "/account"))
        {
            var page = await afterRead.GetInertiaPageAsync();
            Assert.Equal(0, page.Prop("notifications.unreadCount")!.GetValue<int>());
            Assert.Equal(2, page.Prop("notifications.items")!.AsArray().Count);
        }
    }

    [Fact]
    public async Task Direct_insert_is_not_visible_until_the_cache_is_invalidated()
    {
        var (client, uid) = await app.SignUpAsync("cache-direct@example.test");
        using (var first = await app.InertiaGetAsync(client, "/account"))
        {
            var page = await first.GetInertiaPageAsync();
            Assert.Equal(0, page.Prop("notifications.unreadCount")!.GetValue<int>());
        }

        // Bypass the service so the cache is not invalidated.
        await app.QueryAsync(async db =>
        {
            db.Alerts.Add(new Alert { UserId = uid, Kind = "order_status", Title = "Direct insert", Body = "Body", Href = "/account/orders" });
            await db.SaveChangesAsync();
            return true;
        });

        using (var cached = await app.InertiaGetAsync(client, "/account"))
        {
            var page = await cached.GetInertiaPageAsync();
            Assert.Equal(0, page.Prop("notifications.unreadCount")!.GetValue<int>());
        }

        await using (var scope = app.Services.CreateAsyncScope())
            await NotificationsCache.InvalidateAsync(scope.ServiceProvider.GetRequiredService<TechLandier.Infrastructure.IPropCacheInvalidator>(), uid, default);

        using (var refreshed = await app.InertiaGetAsync(client, "/account"))
        {
            var page = await refreshed.GetInertiaPageAsync();
            Assert.Equal(1, page.Prop("notifications.unreadCount")!.GetValue<int>());
        }
    }
}
