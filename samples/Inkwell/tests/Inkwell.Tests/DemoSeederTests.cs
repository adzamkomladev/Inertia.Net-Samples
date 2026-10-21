using Inkwell.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inkwell.Tests;

public class DemoSeederTests(BlogFactory f) : IClassFixture<BlogFactory>
{
    [Fact]
    public async Task Seed_preserves_existing_users_resumes_exact_counts_and_supports_identity_login()
    {
        var existingId = await f.CreateUserAsync(BlogFactory.NewEmail("existing"));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var size = new DemoSeedSize(20,40);
        await DemoSeeder.RunAsync(db,size);
        var demo = await db.Users.AsNoTracking().FirstAsync(u => u.DemoKey == "demo-42-user-00001");
        var hash = demo.PasswordHash;
        Assert.Equal(7,demo.Id.Version);
        Assert.Equal("INERTIA.DEMO42.00001@YOPMAIL.COM",demo.NormalizedEmail);
        Assert.Equal(PasswordVerificationResult.Success,new PasswordHasher<AppUser>().VerifyHashedPassword(demo,demo.PasswordHash!,DemoSeeder.Password));
        Assert.Matches(@"^inertia\.demo42\.\d{5}@yopmail\.com$", demo.Email!);
        Assert.Contains(' ', demo.DisplayName);
        Assert.All(await db.Posts.IgnoreQueryFilters().Where(p => p.DemoKey != null).ToListAsync(), p =>
        {
            Assert.InRange(p.Title.Length, 3, 200);
            Assert.StartsWith("# " + p.Title + "\n\n", p.Body);
            Assert.Contains(DemoSeeder.Topics, t => p.Body.Contains("## Notes on " + t));
        });
        Assert.All(await db.Comments.IgnoreQueryFilters().Select(c => c.Body).ToListAsync(), body => Assert.InRange(body.Length, 1, 5000));
        Assert.True(demo.EmailConfirmed);
        Assert.False(demo.Preferences.DailyRecommendationDigest);
        Assert.True(await db.Users.AnyAsync(u => u.Id == existingId));
        Assert.Equal(20,await db.Users.CountAsync(u => u.IsDemo));
        Assert.Equal(40,await db.Posts.IgnoreQueryFilters().CountAsync(p => p.DemoKey != null));
        Assert.True(await db.Posts.IgnoreQueryFilters().AnyAsync(p => p.DemoKey != null && !p.IsPublished));
        Assert.True(await db.Posts.IgnoreQueryFilters().AnyAsync(p => p.DemoKey != null && p.ArchivedAt != null));
        Assert.Equal(100,await db.Follows.CountAsync());
        Assert.Equal(156,await db.PostLikes.CountAsync());
        Assert.Equal(60,await db.Comments.IgnoreQueryFilters().CountAsync());
        Assert.Equal(120,await db.CommentLikes.CountAsync());
        Assert.Equal(280,await db.SeenPosts.CountAsync());
        Assert.Equal(40,await db.SearchIndexStates.CountAsync());
        var counters = await db.Posts.IgnoreQueryFilters().Select(p => new {p.Id,p.LikeCount,p.CommentCount}).ToListAsync();
        foreach(var p in counters)
        {
            Assert.Equal(await db.PostLikes.CountAsync(l => l.PostId == p.Id),p.LikeCount);
            Assert.Equal(await db.Comments.IgnoreQueryFilters().CountAsync(c => c.PostId == p.Id),p.CommentCount);
        }
        await DemoSeeder.RunAsync(db,size);
        Assert.Equal(hash,await db.Users.Where(u => u.Id == demo.Id).Select(u => u.PasswordHash).SingleAsync());
        Assert.Equal(20,await db.Users.CountAsync(u => u.IsDemo));
        Assert.Equal(40,await db.Posts.IgnoreQueryFilters().CountAsync(p => p.DemoKey != null));
        Assert.Equal(156,await db.PostLikes.CountAsync());
        Assert.Equal(0,await db.Jobs.CountAsync());
        Assert.All(await db.Users.Where(u => u.IsDemo).Select(u => u.Email).ToListAsync(),email => Assert.EndsWith("@yopmail.com",email));
        var client = f.NewClient();
        using var login = await client.LoginAsync(demo.Email!,DemoSeeder.Password);
        Assert.Equal("/posts",login.Headers.Location?.OriginalString);
        // Reproduce a resumed seed whose live worker already created profile/index rows.
        var profile = await db.InterestProfiles.SingleAsync(p => p.UserId == demo.Id);
        profile.TopicsJson = "{\"coding\":1}";
        profile.Dirty = false;
        var state = await db.SearchIndexStates.OrderBy(s => s.PostId).FirstAsync();
        state.RequestedVersion = 7;
        state.AppliedVersion = 6;
        state.TaskId = 123;
        await db.SaveChangesAsync();
        var statePostId = state.PostId;
        await db.SeedCheckpoints.Where(c => c.Name == "demo-42-profiles" || c.Name == "demo-42-search").ExecuteDeleteAsync();
        await db.InterestProfiles.Where(p => p.UserId != demo.Id).ExecuteDeleteAsync();
        await db.SearchIndexStates.Where(s => s.PostId != statePostId).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
        await DemoSeeder.RunAsync(db, size);
        Assert.Equal(20, await db.InterestProfiles.CountAsync());
        Assert.Equal(40, await db.SearchIndexStates.CountAsync());
        profile = await db.InterestProfiles.AsNoTracking().SingleAsync(p => p.UserId == demo.Id);
        Assert.Equal("{\"coding\":1}", profile.TopicsJson);
        Assert.False(profile.Dirty);
        state = await db.SearchIndexStates.AsNoTracking().SingleAsync(s => s.PostId == statePostId);
        Assert.Equal(7, state.RequestedVersion);
        Assert.Equal(6, state.AppliedVersion);
        Assert.Equal(123L, state.TaskId);
        Assert.Equal(20, await db.SeedCheckpoints.Where(c => c.Name == "demo-42-profiles").Select(c => c.Completed).SingleAsync());
        Assert.Equal(40, await db.SeedCheckpoints.Where(c => c.Name == "demo-42-search").Select(c => c.Completed).SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => DemoSeeder.RunAsync(db,new DemoSeedSize(21,40)));
    }

    [Fact]
    public async Task Seed_content_is_deterministic_across_databases()
    {
        static async Task<string[]> Snapshot(BlogFactory factory)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await DemoSeeder.RunAsync(db, new DemoSeedSize(20, 40));
            var names = await db.Users.Where(u => u.IsDemo).OrderBy(u => u.DemoKey).Select(u => u.DisplayName).ToListAsync();
            var posts = await db.Posts.IgnoreQueryFilters().Where(p => p.DemoKey != null).OrderBy(p => p.DemoKey).Select(p => p.Title + p.Body).ToListAsync();
            var comments = await db.Comments.IgnoreQueryFilters().Select(c => c.Body).ToListAsync();
            return [..names, ..posts, ..comments.Order(StringComparer.Ordinal)];
        }
        using var other = new BlogFactory();
        Assert.Equal(await Snapshot(f), await Snapshot(other));
    }
}
