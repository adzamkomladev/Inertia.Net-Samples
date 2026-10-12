using System.Net;
using Inertia.Net.Testing;
using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Features;

namespace VuePgBlog.Tests;

public sealed class SocialTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Relationship_writes_are_idempotent_and_archive_is_private()
    {
        var (owner, ownerId) = await app.SignUpAsync("social-owner@yopmail.com");
        var (reader, readerId) = await app.SignUpAsync("social-reader@yopmail.com");
        using var created = await app.SendAsync(owner, "/posts", new { title = "Social interaction test", body = "Books and technology.", topics = new[] { "Books", "books", "technology" } });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var id = await app.PostIdAsync("Social interaction test");
        var slug = await app.QueryAsync(db => db.Posts.Where(p => p.Id == id).Select(p => p.Slug).SingleAsync());
        foreach (var route in new[] { $"/posts/{id}/bookmark", $"/posts/{id}/repost", $"/posts/{id}/like", $"/users/{ownerId}/follow" })
        {
            using var first = await app.SendAsync(reader, route, new { }, HttpMethod.Put);
            using var repeated = await app.SendAsync(reader, route, new { }, HttpMethod.Put);
            Assert.Equal(HttpStatusCode.SeeOther, first.StatusCode);
            Assert.Equal(HttpStatusCode.SeeOther, repeated.StatusCode);
        }
        Assert.Equal(1, await app.QueryAsync(db => db.Bookmarks.CountAsync(b => b.UserId == readerId && b.PostId == id)));
        Assert.Equal(1, await app.QueryAsync(db => db.Reposts.CountAsync(b => b.UserId == readerId && b.PostId == id)));
        Assert.Equal(1, await app.QueryAsync(db => db.Follows.CountAsync(f => f.FollowerId == readerId && f.FolloweeId == ownerId)));
        Assert.Equal(1, await app.QueryAsync(db => db.Posts.Where(p => p.Id == id).Select(p => p.LikeCount).SingleAsync()));
        Assert.True(await app.QueryAsync(db => db.SeenPosts.AnyAsync(s => s.UserId == readerId && s.PostId == id)));
        Assert.Equal(2, await app.QueryAsync(db => db.PostTopics.CountAsync(t => t.PostId == id)));
        using var comment = await app.SendAsync(reader, $"/posts/{id}/comments", new { body = "Useful reading." });
        Assert.Equal(HttpStatusCode.Redirect, comment.StatusCode);
        var commentId = await app.QueryAsync(db => db.Comments.Where(c => c.PostId == id).Select(c => c.Id).SingleAsync());
        using var like = await app.SendAsync(reader, $"/comments/{commentId}/like", new { }, HttpMethod.Put);
        using var likeAgain = await app.SendAsync(reader, $"/comments/{commentId}/like", new { }, HttpMethod.Put);
        Assert.Equal(1, await app.QueryAsync(db => db.CommentLikes.CountAsync(l => l.CommentId == commentId)));
        using var forbidden = await app.SendAsync(reader, $"/posts/{id}/archive", new { }, HttpMethod.Put);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var archived = await app.SendAsync(owner, $"/posts/{id}/archive", new { }, HttpMethod.Put);
        Assert.Equal(HttpStatusCode.SeeOther, archived.StatusCode);
        using var hidden = await app.InertiaGetAsync(reader, $"/posts/{slug}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var own = await app.InertiaGetAsync(owner, $"/posts/{slug}");
        await own.AssertInertiaAsync(p => p.Component("Posts/Show").Where("post.canArchive", true).Etc());
        using var archives = await app.InertiaGetAsync(owner, "/me/archives");
        await archives.AssertInertiaAsync(p => p.Component("Feed/Index").Has("posts.data", 1).Etc());
        using var restored = await app.SendAsync(owner, $"/posts/{id}/archive", new { }, HttpMethod.Delete);
        Assert.Equal(HttpStatusCode.SeeOther, restored.StatusCode);
        using var visible = await app.InertiaGetAsync(reader, $"/posts/{slug}");
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        foreach (var route in new[] { $"/posts/{id}/bookmark", $"/posts/{id}/repost", $"/posts/{id}/like", $"/users/{ownerId}/follow", $"/comments/{commentId}/like" })
        {
            using var first = await app.SendAsync(reader, route, new { }, HttpMethod.Delete);
            Assert.Equal(HttpStatusCode.SeeOther, first.StatusCode);
        }
        using var repeatedUnlike = await app.SendAsync(reader, $"/posts/{id}/like", new { }, HttpMethod.Delete);
        Assert.Equal(HttpStatusCode.SeeOther, repeatedUnlike.StatusCode);
        Assert.Equal(0, await app.QueryAsync(db => db.Posts.Where(p => p.Id == id).Select(p => p.LikeCount).SingleAsync()));
        Assert.False(await app.QueryAsync(db => db.Bookmarks.AnyAsync(b => b.UserId == readerId && b.PostId == id)));
        Assert.False(await app.QueryAsync(db => db.Reposts.AnyAsync(b => b.UserId == readerId && b.PostId == id)));
        Assert.False(await app.QueryAsync(db => db.Follows.AnyAsync(f => f.FollowerId == readerId && f.FolloweeId == ownerId)));
        Assert.False(await app.QueryAsync(db => db.CommentLikes.AnyAsync(l => l.CommentId == commentId && l.UserId == readerId)));
        using var selfFollow = await app.SendAsync(reader, $"/users/{readerId}/follow", new { }, HttpMethod.Put);
        Assert.Equal(HttpStatusCode.BadRequest, selfFollow.StatusCode);
    }

    [Fact]
    public async Task Cursor_order_survives_new_posts_and_rejects_invalid_values()
    {
        var (client, uid) = await app.SignUpAsync("social-cursor@yopmail.com");
        await app.QueryAsync(async db =>
        {
            var at = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            db.Posts.AddRange(Enumerable.Range(1, 12).Select(i => new Post { AuthorId = uid, Title = $"Cursor {i}", Body = "test", Excerpt = "test", Slug = $"cursor-{i}", CreatedAt = at.AddMinutes(i) }));
            await db.SaveChangesAsync();
            return true;
        });
        using var response = await app.InertiaGetAsync(client, "/me/posts");
        var page = await InertiaPage.ParseAsync(response);
        var next = page.Props["posts"]!["nextCursor"]!.GetValue<string>();
        var firstIds = page.Props["posts"]!["data"]!.AsArray().Select(p => p!["id"]!.GetValue<Guid>()).ToHashSet();
        await app.QueryAsync(async db =>
        {
            db.Posts.Add(new Post { AuthorId = uid, Title = "Inserted after cursor", Body = "test", Excerpt = "test", Slug = "after-cursor" });
            await db.SaveChangesAsync();
            return true;
        });
        using var nextResponse = await app.InertiaGetAsync(client, "/me/posts?cursor=" + Uri.EscapeDataString(next));
        var second = await InertiaPage.ParseAsync(nextResponse);
        Assert.Equal(2, second.Props["posts"]!["data"]!.AsArray().Count);
        Assert.All(second.Props["posts"]!["data"]!.AsArray(), p => Assert.DoesNotContain(p!["id"]!.GetValue<Guid>(), firstIds));
        using var malformed = await app.InertiaGetAsync(client, "/posts?cursor=malformed");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        var cursorId = Guid.CreateVersion7();
        Assert.True(SocialHelpers.TryCursor(SocialHelpers.Cursor(DateTime.UtcNow, cursorId), out var edge));
        Assert.Equal(cursorId, edge!.Value.Id);
        Assert.False(SocialHelpers.TryCursor(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"1:{Guid.Empty}")), out _));
        Assert.False(SocialHelpers.TryCursor(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("1:10")), out _));
    }
    [Fact]
    public async Task Views_only_mark_seen_on_beacon_and_comments_are_paginated()
    {
        var (owner, uid) = await app.SignUpAsync("social-beacon@yopmail.com");
        using var created = await app.SendAsync(owner, "/posts", new { title = "Beacon pagination post", body = "test" });
        var id = await app.PostIdAsync("Beacon pagination post");
        var slug = await app.QueryAsync(db => db.Posts.Where(p => p.Id == id).Select(p => p.Slug).SingleAsync());
        using var opened = await app.InertiaGetAsync(owner, $"/posts/{slug}");
        Assert.False(await app.QueryAsync(db => db.SeenPosts.AnyAsync(s => s.UserId == uid && s.PostId == id)));
        using var beacon = await app.SendAsync(owner, $"/posts/{id}/view", new { });
        Assert.Equal(HttpStatusCode.NoContent, beacon.StatusCode);
        Assert.True(await app.QueryAsync(db => db.SeenPosts.AnyAsync(s => s.UserId == uid && s.PostId == id)));
        await app.QueryAsync(async db =>
        {
            var at = DateTime.UtcNow;
            db.Comments.AddRange(Enumerable.Range(0, 51).Select(i => new Comment { PostId = id, UserId = uid, Body = $"Comment {i}", CreatedAt = at }));
            await db.SaveChangesAsync();
            return true;
        });
        using var page = await owner.GetAsync($"/posts/{id}/comments");
        var json = System.Text.Json.Nodes.JsonNode.Parse(await page.Content.ReadAsStringAsync())!;
        Assert.Equal(50, json["data"]!.AsArray().Count);
        var cursor = json["nextCursor"]!.GetValue<string>();
        using var next = await owner.GetAsync($"/posts/{id}/comments?cursor={Uri.EscapeDataString(cursor)}");
        var nextJson = System.Text.Json.Nodes.JsonNode.Parse(await next.Content.ReadAsStringAsync())!;
        Assert.Single(nextJson["data"]!.AsArray());
        using var archived = await app.SendAsync(owner, $"/posts/{id}/archive", new { }, HttpMethod.Put);
        using var publicComments = await app.Client().GetAsync($"/posts/{id}/comments");
        Assert.Equal(HttpStatusCode.NotFound, publicComments.StatusCode);
        using var ownComments = await owner.GetAsync($"/posts/{id}/comments");
        Assert.Equal(HttpStatusCode.OK, ownComments.StatusCode);
    }

}
