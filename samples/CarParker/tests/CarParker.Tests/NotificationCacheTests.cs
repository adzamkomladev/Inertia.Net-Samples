using CarParker.Data;
using CarParker.Features.Alerts;
using Inertia.Net.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CarParker.Tests;

/// The shared alert summary is cached per user and every write path refreshes it.
public sealed class NotificationCacheTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Shared_summary_is_cached_per_user_and_invalidated_by_writes()
    {
        var (client, uid) = await app.SeedUserAsync();
        var (other, _) = await app.SeedUserAsync(UserRole.Guard);
        await using var scope = app.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<AlertsService>();

        using (var initial = await app.InertiaGetAsync(client, "/account"))
        {
            var page = await initial.GetInertiaPageAsync();
            Assert.Equal(0, page.Prop("notifications.unreadCount")!.GetValue<int>());
        }

        await svc.CreateAsync(uid, "refund", "First cached alert", "Body", sourceKey: "cache-first");
        using (var afterFirst = await app.InertiaGetAsync(client, "/account"))
        {
            var page = await afterFirst.GetInertiaPageAsync();
            Assert.Equal(1, page.Prop("notifications.unreadCount")!.GetValue<int>());
            Assert.Single(page.Prop("notifications.items")!.AsArray());
        }

        await svc.CreateAsync(uid, "refund", "Second cached alert", "Body", sourceKey: "cache-second");
        using (var afterSecond = await app.InertiaGetAsync(client, "/account"))
        {
            var page = await afterSecond.GetInertiaPageAsync();
            Assert.Equal(2, page.Prop("notifications.unreadCount")!.GetValue<int>());
            Assert.Equal(2, page.Prop("notifications.items")!.AsArray().Count);
        }

        // The cache key is per user: the guard's summary never shows the customer's alerts.
        using (var otherPage = await app.InertiaGetAsync(other, "/account"))
        {
            var page = await otherPage.GetInertiaPageAsync();
            Assert.Equal(0, page.Prop("notifications.unreadCount")!.GetValue<int>());
            Assert.Empty(page.Prop("notifications.items")!.AsArray());
        }

        await svc.MarkAllReadAsync(uid, default);
        using (var afterRead = await app.InertiaGetAsync(client, "/account"))
        {
            var page = await afterRead.GetInertiaPageAsync();
            Assert.Equal(0, page.Prop("notifications.unreadCount")!.GetValue<int>());
            Assert.Equal(2, page.Prop("notifications.items")!.AsArray().Count);
        }
    }
}
