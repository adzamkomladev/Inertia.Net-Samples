using System.Net;
using System.Net.Http.Json;
using Inertia.Net.Testing;
using ReactSqliteBlog.Data;
using Microsoft.EntityFrameworkCore;

namespace ReactSqliteBlog.Tests;

public class PostsTests(BlogFactory f) : IClassFixture<BlogFactory>
{
    private async Task<BlogFactory.Client> VerifiedClientAsync(string prefix = "v")
    {
        var email = BlogFactory.NewEmail(prefix);
        await f.CreateUserAsync(email);
        var c = f.NewClient();
        await c.LoginOkAsync(email);
        return c;
    }

    private Post? GetPost(Guid id) => f.WithDb(db => db.Posts.AsNoTracking().FirstOrDefault(p => p.Id == id));
    private static bool Denied(HttpResponseMessage r) =>
        r.StatusCode == HttpStatusCode.Forbidden || r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther;

    [Fact]
    public async Task Hover_prefetch_does_not_count_but_opening_cached_post_records_one_view()
    {
        var author = await VerifiedClientAsync();
        var id = await author.CreatePostAsync("Prefetched post", "body");
        var visitor = f.NewClient();
        visitor.Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        visitor.Http.DefaultRequestHeaders.Add("X-Inertia-Prefetch", "true");
        using var prefetched = await visitor.GetAsync($"/posts/{id}");
        Assert.Equal(HttpStatusCode.OK, prefetched.StatusCode);
        Assert.Equal(0, GetPost(id)!.ViewCount);
        visitor.Http.DefaultRequestHeaders.Remove("X-Inertia-Prefetch");
        using var opened = await visitor.PostAsync($"/posts/{id}/view", new { });
        Assert.Equal(HttpStatusCode.NoContent, opened.StatusCode);
        using var repeated = await visitor.PostAsync($"/posts/{id}/view", new { });
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        Assert.Equal(1, GetPost(id)!.ViewCount);
    }

    [Fact]
    public async Task Unverified_user_cannot_create_post()
    {
        var email = BlogFactory.NewEmail();
        await f.CreateUserAsync(email, verified: false);
        var c = f.NewClient();
        await c.LoginOkAsync(email);
        var title = "unverified-" + Guid.NewGuid().ToString("N");
        using var r = await c.PostAsync("/posts", new { title, body = "body" });
        Assert.True(Denied(r), r.StatusCode.ToString());
        Assert.DoesNotContain("/posts/", r.Headers.Location?.OriginalString ?? "");
        Assert.False(f.WithDb(db => db.Posts.Any(p => p.Title == title)));
    }

    [Fact]
    public async Task Verified_user_creates_post_and_sees_it()
    {
        var c = await VerifiedClientAsync();
        var id = await c.CreatePostAsync("Hello", "World body");
        using var page = await c.GetAsync($"/posts/{id}");
        await page.AssertInertiaAsync(p => p.Component("Posts/Show")
            .Where("post.id", id).Where("post.title", "Hello").Where("post.body", "World body")
            .Where("post.likeCount", 0).Where("canEdit", true).Where("liked", false));

        // Comments are deferred: they arrive on the follow-up partial request.
        c.Http.DefaultRequestHeaders.Add("X-Inertia-Partial-Component", "Posts/Show");
        c.Http.DefaultRequestHeaders.Add("X-Inertia-Partial-Data", "comments");
        using var deferred = await c.GetAsync($"/posts/{id}");
        await deferred.AssertInertiaAsync(p => p.Component("Posts/Show").Has("comments", 0));
    }

    [Fact]
    public async Task Other_user_cannot_update_or_delete()
    {
        var author = await VerifiedClientAsync("a");
        var other = await VerifiedClientAsync("o");
        var id = await author.CreatePostAsync("Mine", "original");

        using var put = await other.SendAsync(HttpMethod.Put, $"/posts/{id}", new { title = "Hacked", body = "hacked" });
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        using var del = await other.SendAsync(HttpMethod.Delete, $"/posts/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, del.StatusCode);

        var post = GetPost(id)!;
        Assert.Equal("Mine", post.Title);
        Assert.Equal("original", post.Body);
    }

    [Fact]
    public async Task Author_can_update_and_delete()
    {
        var c = await VerifiedClientAsync();
        var id = await c.CreatePostAsync("Old", "old");
        using var put = await c.SendAsync(HttpMethod.Put, $"/posts/{id}", new { title = "New", body = "new body" });
        Assert.Equal($"/posts/{id}", put.Headers.Location!.OriginalString);
        Assert.Equal("New", GetPost(id)!.Title);

        using var del = await c.SendAsync(HttpMethod.Delete, $"/posts/{id}");
        Assert.Equal("/posts", del.Headers.Location!.OriginalString);
        Assert.Null(GetPost(id));
    }

    [Fact]
    public async Task Invalid_post_returns_validation_errors()
    {
        var c = await VerifiedClientAsync();
        using var r = await c.PostAsync("/posts", new { title = "", body = "" });
        Assert.True(Denied(r));
        using var next = await c.GetAsync(r.Headers.Location!.OriginalString);
        await next.AssertInertiaAsync(p => p.Has("errors", e => e.Has("title").Etc()));
    }

    [Fact]
    public async Task Comment_can_be_deleted_by_its_author_and_the_post_author_but_not_a_third_user()
    {
        var author = await VerifiedClientAsync("a");
        var commenter = await VerifiedClientAsync("c");
        var third = await VerifiedClientAsync("t");
        var id = await author.CreatePostAsync("P", "body");

        async Task<Guid> Comment()
        {
            using var r = await commenter.PostAsync($"/posts/{id}/comments", new { body = "nice " + Guid.NewGuid() });
            return f.WithDb(db => db.Comments.Where(x => x.PostId == id).OrderByDescending(x => x.Id).First().Id);
        }

        var c1 = await Comment();
        using (var r = await third.SendAsync(HttpMethod.Delete, $"/comments/{c1}")) Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.True(f.WithDb(db => db.Comments.Any(x => x.Id == c1)));

        using (var r = await commenter.SendAsync(HttpMethod.Delete, $"/comments/{c1}")) Assert.True(Denied(r));
        Assert.False(f.WithDb(db => db.Comments.Any(x => x.Id == c1)));

        var c2 = await Comment();
        using (var r = await author.SendAsync(HttpMethod.Delete, $"/comments/{c2}")) Assert.True(Denied(r));
        Assert.False(f.WithDb(db => db.Comments.Any(x => x.Id == c2)));
        Assert.Equal(0, GetPost(id)!.CommentCount);
    }

    [Fact]
    public async Task Like_toggles_and_notifies_only_when_liker_is_not_the_author()
    {
        var author = await VerifiedClientAsync("a");
        var fan = await VerifiedClientAsync("f");
        var id = await author.CreatePostAsync("Likeable", "body");
        int Notifications() => f.WithDb(db => db.Jobs.Count(j => j.Type == "LikeNotification" && j.Payload.Contains($"\"postId\":\"{id}\",")));

        using (await fan.PostAsync($"/posts/{id}/like")) { }
        Assert.Equal(1, GetPost(id)!.LikeCount);
        Assert.Equal(1, Notifications());
        using (await fan.PostAsync($"/posts/{id}/like")) { }
        Assert.Equal(0, GetPost(id)!.LikeCount);

        using (await author.PostAsync($"/posts/{id}/like")) { }
        Assert.Equal(1, GetPost(id)!.LikeCount);
        Assert.Equal(1, Notifications()); // self-like: no new job
    }

    [Fact]
    public async Task Comment_increments_count_and_enqueues_notification_and_empty_body_is_rejected()
    {
        var author = await VerifiedClientAsync("a");
        var other = await VerifiedClientAsync("o");
        var id = await author.CreatePostAsync("Chatty", "body");

        using (var r = await other.PostAsync($"/posts/{id}/comments", new { body = "first!" })) Assert.True(Denied(r));
        Assert.Equal(1, GetPost(id)!.CommentCount);
        var commentId = f.WithDb(db => db.Comments.Single(c => c.PostId == id).Id);
        Assert.True(f.WithDb(db => db.Jobs.Any(j => j.Type == "CommentNotification" && j.Payload.Contains($":\"{commentId}\"}}"))));

        using var bad = await other.PostAsync($"/posts/{id}/comments", new { body = "" }, referer: $"/posts/{id}");
        Assert.True(Denied(bad));
        using var next = await other.GetAsync(bad.Headers.Location!.OriginalString);
        await next.AssertInertiaAsync(p => p.Has("errors.body"));
        Assert.Equal(1, GetPost(id)!.CommentCount);
    }

    [Fact]
    public async Task Antiforgery_missing_header_is_rejected_with_no_effect()
    {
        var c = await VerifiedClientAsync();
        var title = "csrf-" + Guid.NewGuid().ToString("N");
        using var inertia = await c.SendAsync(HttpMethod.Post, "/posts", new { title, body = "x" }, withToken: false);
        Assert.True(inertia.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.BadRequest);
        Assert.DoesNotContain("/posts/", inertia.Headers.Location?.OriginalString ?? "");

        var plain = new HttpRequestMessage(HttpMethod.Post, "/posts") { Content = JsonContent.Create(new { title, body = "x" }) };
        using var r = await c.Http.SendAsync(plain);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.False(f.WithDb(db => db.Posts.Any(p => p.Title == title)));
    }
}
