using Inertia.Net.Testing;
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
}
