using System.Net;
using Inertia.Net;
using Inertia.Net.Testing;
using Inkwell.Features.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inkwell.Tests;

public sealed class NotificationTests(BlogFactory app) : IClassFixture<BlogFactory>
{
    [Fact]
    public async Task Likes_create_one_private_durable_notification_and_read_all_is_scoped()
    {
        var ownerEmail = BlogFactory.NewEmail("notice-owner");
        var ownerId = await app.CreateUserAsync(ownerEmail);
        var readerEmail = BlogFactory.NewEmail("notice-reader");
        await app.CreateUserAsync(readerEmail);
        var owner = app.NewClient(); await owner.LoginOkAsync(ownerEmail);
        var reader = app.NewClient(); await reader.LoginOkAsync(readerEmail);
        var post = await owner.CreatePostAsync("Notification test", "Body");
        using var liked = await reader.SendAsync(HttpMethod.Put, $"/posts/{post}/like");
        using var duplicate = await reader.SendAsync(HttpMethod.Put, $"/posts/{post}/like");
        Assert.Equal(HttpStatusCode.SeeOther, liked.StatusCode);
        Assert.Equal(1, app.WithDb(db => db.Notifications.Count(n => n.UserId == ownerId && n.ReadAt == null)));
        using var inbox = await owner.GetAsync("/notifications");
        await inbox.AssertInertiaAsync(p => p.Component("Notifications/Index").Has("items", 1).Where("notifications.unreadCount", 1));
        using var stranger = await reader.GetAsync("/notifications");
        await stranger.AssertInertiaAsync(p => p.Has("items", 0).Where("notifications.unreadCount", 0));
        using var strangerRead = await reader.PostAsync("/notifications/read-all");
        Assert.Equal(1, app.WithDb(db => db.Notifications.Count(n => n.UserId == ownerId && n.ReadAt == null)));
        using var comment = await reader.PostAsync($"/posts/{post}/comments", new { body = "An inbox reply" });
        Assert.Equal(2, app.WithDb(db => db.Notifications.Count(n => n.UserId == ownerId && n.ReadAt == null)));
        using var selfComment = await owner.PostAsync($"/posts/{post}/comments", new { body = "My own reply" });
        Assert.Equal(2, app.WithDb(db => db.Notifications.Count(n => n.UserId == ownerId)));
        using var read = await owner.PostAsync("/notifications/read-all");
        Assert.Equal(0, app.WithDb(db => db.Notifications.Count(n => n.UserId == ownerId && n.ReadAt == null)));
    }

    [Fact]
    public async Task Shared_notification_summary_is_cached_per_user_and_invalidated_by_writes()
    {
        var ownerEmail = BlogFactory.NewEmail("cache-owner");
        await app.CreateUserAsync(ownerEmail);
        var readerEmail = BlogFactory.NewEmail("cache-reader");
        await app.CreateUserAsync(readerEmail);
        var owner = app.NewClient(); await owner.LoginOkAsync(ownerEmail);
        var reader = app.NewClient(); await reader.LoginOkAsync(readerEmail);
        var post = await owner.CreatePostAsync("Cached notification", "Body");

        using (var initial = await owner.GetAsync("/posts"))
            await initial.AssertInertiaAsync(p => p.Where("notifications.unreadCount", 0).Has("notifications.items", 0));

        using (var liked = await reader.SendAsync(HttpMethod.Put, $"/posts/{post}/like"))
            Assert.Equal(HttpStatusCode.SeeOther, liked.StatusCode);

        using (var afterLike = await owner.GetAsync("/posts"))
            await afterLike.AssertInertiaAsync(p => p.Where("notifications.unreadCount", 1).Has("notifications.items", 1));

        using (var commented = await reader.PostAsync($"/posts/{post}/comments", new { body = "A cached reply" }))
            Assert.True(commented.IsSuccessStatusCode || (int)commented.StatusCode is 302 or 303);

        using (var afterComment = await owner.GetAsync("/posts"))
            await afterComment.AssertInertiaAsync(p => p.Where("notifications.unreadCount", 2).Has("notifications.items", 2));

        // The cache key is per user: the reader's shared summary never shows the owner's notifications.
        using (var readerPage = await reader.GetAsync("/posts"))
            await readerPage.AssertInertiaAsync(p => p.Where("notifications.unreadCount", 0).Has("notifications.items", 0));

        using (var read = await owner.PostAsync("/notifications/read-all"))
            Assert.True(read.IsSuccessStatusCode || (int)read.StatusCode is 302 or 303);

        using (var afterRead = await owner.GetAsync("/posts"))
            await afterRead.AssertInertiaAsync(p => p.Where("notifications.unreadCount", 0).Has("notifications.items", 2));
    }

    [Fact]
    public async Task Shared_notification_summary_serves_the_cached_value_until_invalidated()
    {
        var email = BlogFactory.NewEmail("cache-hit");
        var id = await app.CreateUserAsync(email);
        var client = app.NewClient(); await client.LoginOkAsync(email);

        using (var first = await client.GetAsync("/posts"))
            await first.AssertInertiaAsync(p => p.Where("notifications.unreadCount", 0).Has("notifications.items", 0));

        // Bypass the service so the cache is not invalidated.
        app.WithDb(db =>
        {
            db.Notifications.Add(new Notification { UserId = id, Title = "Direct insert", Href = "/posts" });
            db.SaveChanges();
            return true;
        });

        using (var cached = await client.GetAsync("/posts"))
            await cached.AssertInertiaAsync(p => p.Where("notifications.unreadCount", 0).Has("notifications.items", 0));

        using (var scope = app.Services.CreateAsyncScope())
            await NotificationsCache.InvalidateAsync(scope.ServiceProvider.GetRequiredService<IInertiaCache>(), id, default);

        using (var refreshed = await client.GetAsync("/posts"))
            await refreshed.AssertInertiaAsync(p => p.Where("notifications.unreadCount", 1).Has("notifications.items", 1));
    }
}
