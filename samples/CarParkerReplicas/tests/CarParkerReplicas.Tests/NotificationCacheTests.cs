using CarParker.Data;
using CarParker.Features.Alerts;
using Inertia.Net;
using Inertia.Net.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

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

    [Fact]
    public async Task Direct_insert_is_not_visible_until_the_cache_is_invalidated()
    {
        var (client, uid) = await app.SeedUserAsync();
        using (var first = await app.InertiaGetAsync(client, "/account"))
        {
            var page = await first.GetInertiaPageAsync();
            Assert.Equal(0, page.Prop("notifications.unreadCount")!.GetValue<int>());
        }

        // Bypass the service so the cache is not invalidated.
        await app.QueryAsync(async db =>
        {
            await db.Alerts.InsertOneAsync(new Alert { UserId = uid, Kind = "refund", Title = "Direct insert" });
            return true;
        });

        using (var cached = await app.InertiaGetAsync(client, "/account"))
        {
            var page = await cached.GetInertiaPageAsync();
            Assert.Equal(0, page.Prop("notifications.unreadCount")!.GetValue<int>());
        }

        await using (var scope = app.Services.CreateAsyncScope())
            await AlertsCache.InvalidateAsync(scope.ServiceProvider.GetRequiredService<IInertiaCache>(), uid, default);

        using (var refreshed = await app.InertiaGetAsync(client, "/account"))
        {
            var page = await refreshed.GetInertiaPageAsync();
            Assert.Equal(1, page.Prop("notifications.unreadCount")!.GetValue<int>());
        }
    }

    [Fact]
    public async Task Mark_read_and_create_many_refresh_the_cached_summary()
    {
        var (client, uid) = await app.SeedUserAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<AlertsService>();

        await svc.CreateManyAsync(
        [
            AlertsService.NewAlert(uid, "refund", "First batch alert"),
            AlertsService.NewAlert(uid, "refund", "Second batch alert"),
        ], default);

        using (var first = await app.InertiaGetAsync(client, "/account"))
        {
            var page = await first.GetInertiaPageAsync();
            Assert.Equal(2, page.Prop("notifications.unreadCount")!.GetValue<int>());
        }

        var ids = await app.QueryAsync(async db => await db.Alerts.Find(a => a.UserId == uid).Project(a => a.Id).ToListAsync());
        await svc.MarkReadAsync(uid, [ids[0]], default);
        using (var afterRead = await app.InertiaGetAsync(client, "/account"))
        {
            var page = await afterRead.GetInertiaPageAsync();
            Assert.Equal(1, page.Prop("notifications.unreadCount")!.GetValue<int>());
        }

        await svc.CreateManyAsync([AlertsService.NewAlert(uid, "refund", "Third batch alert")], default);
        using (var afterMany = await app.InertiaGetAsync(client, "/account"))
        {
            var page = await afterMany.GetInertiaPageAsync();
            Assert.Equal(2, page.Prop("notifications.unreadCount")!.GetValue<int>());
        }
    }
}
