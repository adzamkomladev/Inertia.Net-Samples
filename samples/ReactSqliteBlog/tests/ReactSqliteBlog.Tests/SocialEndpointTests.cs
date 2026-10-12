using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ReactSqliteBlog.Data;
using Inertia.Net.Testing;
using Microsoft.EntityFrameworkCore;

namespace ReactSqliteBlog.Tests;

public class SocialEndpointTests(BlogFactory f) : IClassFixture<BlogFactory>
{
    async Task<(BlogFactory.Client Client,Guid Id)> Reader(string prefix)
    {
        var email = BlogFactory.NewEmail(prefix);
        var id = await f.CreateUserAsync(email);
        var c = f.NewClient(); await c.LoginOkAsync(email); return (c,id);
    }
    [Fact]
    public async Task Relationships_are_idempotent_and_archive_is_owner_only()
    {
        var (owner,oid) = await Reader("social-owner");
        var (reader,rid) = await Reader("social-reader");
        var post = await owner.CreatePostAsync("social post","body");
        foreach(var route in new[] {$"/posts/{post}/bookmark",$"/posts/{post}/repost",$"/users/{oid}/follow",$"/posts/{post}/like"})
        {
            using var one = await reader.SendAsync(HttpMethod.Put,route); Assert.Equal(HttpStatusCode.SeeOther,one.StatusCode);
            using var two = await reader.SendAsync(HttpMethod.Put,route); Assert.Equal(HttpStatusCode.SeeOther,two.StatusCode);
        }
        using(var self = await reader.SendAsync(HttpMethod.Put,$"/users/{rid}/follow")) Assert.Equal(HttpStatusCode.BadRequest,self.StatusCode);
        using(var comment = await owner.PostAsync($"/posts/{post}/comments",new { body = "a liked comment" })) Assert.Equal(HttpStatusCode.Redirect,comment.StatusCode);
        var commentId = f.WithDb(db => db.Comments.Where(c => c.PostId == post).Select(c => c.Id).Single());
        using(var firstLike = await reader.SendAsync(HttpMethod.Put,$"/comments/{commentId}/like")) Assert.Equal(HttpStatusCode.SeeOther,firstLike.StatusCode);
        using(var againLike = await reader.SendAsync(HttpMethod.Put,$"/comments/{commentId}/like")) Assert.Equal(HttpStatusCode.SeeOther,againLike.StatusCode);
        Assert.Equal(1,f.WithDb(db => db.CommentLikes.Count(c => c.CommentId == commentId && c.UserId == rid)));
        Assert.Equal(1,f.WithDb(db => db.Bookmarks.Count(b => b.UserId == rid && b.PostId == post)));
        Assert.Equal(1,f.WithDb(db => db.Reposts.Count(b => b.UserId == rid && b.PostId == post)));
        Assert.Equal(1,f.WithDb(db => db.Posts.Single(p => p.Id == post).LikeCount));
        using(var forbidden = await reader.SendAsync(HttpMethod.Put,$"/posts/{post}/archive")) Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
        using(var archived = await owner.SendAsync(HttpMethod.Put,$"/posts/{post}/archive")) Assert.Equal(HttpStatusCode.SeeOther,archived.StatusCode);
        using(var own = await owner.GetAsync($"/posts/{post}")) Assert.Equal(HttpStatusCode.OK,own.StatusCode);
        using(var hidden = await reader.GetAsync($"/posts/{post}")) Assert.Equal(HttpStatusCode.NotFound,hidden.StatusCode);
        using(var archives = await owner.GetAsync("/me/archives"))
            await archives.AssertInertiaAsync(p => p.Component("Feed/Index").Has("posts.data",1).Where("posts.data.0.id",post));
        using(var mine = await reader.GetAsync("/me/posts"))
            await mine.AssertInertiaAsync(p => p.Has("posts.data",0));
        using(var profile = await reader.GetAsync($"/users/{oid}"))
            await profile.AssertInertiaAsync(p => p.Component("Users/Show").Has("posts.data",0));
        using(var restore = await owner.SendAsync(HttpMethod.Delete,$"/posts/{post}/archive")) Assert.Equal(HttpStatusCode.SeeOther,restore.StatusCode);
        using(var visible = await reader.GetAsync($"/posts/{post}")) Assert.Equal(HttpStatusCode.OK,visible.StatusCode);
        using(var remove = await reader.SendAsync(HttpMethod.Delete,$"/posts/{post}/like")) Assert.Equal(HttpStatusCode.SeeOther,remove.StatusCode);
        using(var again = await reader.SendAsync(HttpMethod.Delete,$"/posts/{post}/like")) Assert.Equal(HttpStatusCode.SeeOther,again.StatusCode);
        Assert.Equal(0,f.WithDb(db => db.Posts.Single(p => p.Id == post).LikeCount));
    }
    [Fact]
    public async Task Opening_beacon_marks_seen_but_prefetch_does_not_and_comments_are_cursor_bounded()
    {
        var (owner,oid) = await Reader("comment-owner"); var (reader,rid) = await Reader("comment-reader");
        var post = await owner.CreatePostAsync("many comments","body");
        f.WithDb(db => {
            var now = DateTime.UtcNow;
            db.Comments.AddRange(Enumerable.Range(0,55).Select(i => new Comment {PostId = post,AuthorId = oid,Body = $"comment {i}",CreatedAt = now.AddSeconds(-i)}));
            return db.SaveChanges();
        });
        reader.Http.DefaultRequestHeaders.Add("X-Inertia-Prefetch","true");
        using(var prefetched = await reader.GetAsync($"/posts/{post}")) Assert.Equal(HttpStatusCode.OK,prefetched.StatusCode);
        Assert.False(f.WithDb(db => db.SeenPosts.Any(s => s.PostId == post && s.UserId == rid)));
        reader.Http.DefaultRequestHeaders.Remove("X-Inertia-Prefetch");
        using(var beacon = await reader.PostAsync($"/posts/{post}/view")) Assert.Equal(HttpStatusCode.NoContent,beacon.StatusCode);
        Assert.True(f.WithDb(db => db.SeenPosts.Any(s => s.PostId == post && s.UserId == rid)));
        using var first = await reader.GetAsync($"/posts/{post}/comments");
        var json = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(50,json.GetProperty("data").GetArrayLength());
        var cursor = json.GetProperty("nextCursor").GetString();
        using var next = await reader.GetAsync($"/posts/{post}/comments?cursor={Uri.EscapeDataString(cursor!)}");
        var rest = await next.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(5,rest.GetProperty("data").GetArrayLength());
    }
}
