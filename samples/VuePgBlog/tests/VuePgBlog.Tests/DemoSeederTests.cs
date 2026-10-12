using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VuePgBlog.Data;

namespace VuePgBlog.Tests;

public class DemoSeederTests(AppFactory app) : IClassFixture<AppFactory>
{
    static readonly DemoSeedSize Size = new(20, 40);

    [Fact]
    public async Task Seed_writes_exact_counts_resumes_idempotently_and_supports_login()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await DemoSeeder.RunAsync(db, Size);

        var demo = await db.Users.AsNoTracking().SingleAsync(u => u.DemoKey == "demo-42-user-00001");
        Assert.Equal(7, demo.Id.Version);
        Assert.Matches(@"^inertia\.demo42\.\d{5}@yopmail\.com$", demo.Email);
        Assert.Contains(' ', demo.Name);
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<User>().VerifyHashedPassword(demo, demo.PasswordHash, DemoSeeder.Password));
        Assert.False(demo.Preferences.WeeklyDigest);
        Assert.False(demo.Preferences.EmailOnComment);
        Assert.NotEmpty(demo.Preferences.TimeZoneId);
        Assert.Equal(20, await db.Users.CountAsync(u => u.IsDemo));
        Assert.Equal(40, await db.Posts.IgnoreQueryFilters().CountAsync(p => p.DemoKey != null));
        Assert.All(await db.Posts.IgnoreQueryFilters().Where(p => p.DemoKey != null).ToListAsync(), p =>
        {
            Assert.InRange(p.Title.Length, 3, 200);
            Assert.StartsWith("# " + p.Title + "\n\n", p.Body);
            Assert.Contains(DemoSeeder.Topics, t => p.Body.Contains("## Notes on " + t));
            Assert.InRange(p.Excerpt.Length, 1, 210);
            Assert.DoesNotContain('#', p.Excerpt);
            Assert.Contains(p.Slug.Split('-')[2], DemoSeeder.Topics);
        });
        Assert.Equal(100, await db.Follows.CountAsync());
        Assert.Equal(156, await db.Likes.CountAsync());
        Assert.Equal(60, await db.Comments.IgnoreQueryFilters().CountAsync());
        Assert.All(await db.Comments.IgnoreQueryFilters().Select(c => c.Body).ToListAsync(), body => Assert.InRange(body.Length, 1, 2000));
        Assert.Equal(120, await db.CommentLikes.CountAsync());
        Assert.Equal(280, await db.SeenPosts.CountAsync());
        Assert.Equal(20, await db.InterestProfiles.CountAsync());
        Assert.Equal(40, await db.SearchIndexStates.CountAsync());
        foreach (var p in await db.Posts.IgnoreQueryFilters().Select(p => new { p.Id, p.LikeCount, p.CommentCount }).ToListAsync())
        {
            Assert.Equal(await db.Likes.CountAsync(l => l.PostId == p.Id), p.LikeCount);
            Assert.Equal(await db.Comments.IgnoreQueryFilters().CountAsync(c => c.PostId == p.Id), p.CommentCount);
        }

        await DemoSeeder.RunAsync(db, Size);
        Assert.Equal(demo.PasswordHash, await db.Users.Where(u => u.Id == demo.Id).Select(u => u.PasswordHash).SingleAsync());
        Assert.Equal(20, await db.Users.CountAsync(u => u.IsDemo));
        Assert.Equal(156, await db.Likes.CountAsync());
        Assert.Equal(60, await db.Comments.IgnoreQueryFilters().CountAsync());

        var client = app.Client();
        using var login = await app.SendAsync(client, "/login", new { email = demo.Email, password = DemoSeeder.Password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/posts", login.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Seed_content_is_deterministic_across_databases()
    {
        static async Task<string[]> Snapshot(AppFactory factory)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await DemoSeeder.RunAsync(db, Size);
            var names = await db.Users.Where(u => u.IsDemo).OrderBy(u => u.DemoKey).Select(u => u.Name).ToListAsync();
            var posts = await db.Posts.IgnoreQueryFilters().Where(p => p.DemoKey != null).OrderBy(p => p.DemoKey).Select(p => p.Title + p.Body + p.Excerpt).ToListAsync();
            var comments = await db.Comments.IgnoreQueryFilters().Select(c => c.Body).ToListAsync();
            return [.. names, .. posts, .. comments.Order(StringComparer.Ordinal)];
        }
        await using var other = new AppFactory();
        Assert.Equal(await Snapshot(app), await Snapshot(other));
    }
}
