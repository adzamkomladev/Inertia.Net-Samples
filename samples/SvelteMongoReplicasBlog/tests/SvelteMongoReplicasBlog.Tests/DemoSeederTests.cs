using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;

namespace SvelteMongoReplicasBlog.Tests;

public class DemoSeederTests(AppFactory app) : IClassFixture<AppFactory>
{
    static readonly DemoSeedSize Size = new(20, 40);

    [Fact]
    public async Task Seed_writes_exact_counts_generated_content_and_is_idempotent()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await DemoSeeder.RunAsync(db, Size);

        var demo = await db.Users.Find(x => x.DemoKey == "demo-42-user-00001").SingleAsync();
        Assert.Matches(@"^inertia\.demo42\.\d{5}@yopmail\.com$", demo.Email);
        Assert.Contains(' ', demo.Name);
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<User>().VerifyHashedPassword(demo, demo.PasswordHash, DemoSeeder.Password));
        Assert.Equal(20, await db.Users.CountDocumentsAsync(x => x.IsDemo));
        var posts = await db.Posts.Find(x => x.DemoKey != null && x.DemoKey.StartsWith("demo-42-post-")).ToListAsync();
        Assert.Equal(40, posts.Count);
        Assert.All(posts, p =>
        {
            Assert.InRange(p.Title.Length, 3, 200);
            Assert.StartsWith("# " + p.Title + "\n\n", p.Body);
            Assert.Contains(DemoSeeder.Topics, t => p.Body.Contains("## Notes on " + t));
            Assert.InRange(p.Excerpt.Length, 1, 210);
            Assert.Contains(p.Slug.Split('-')[2], DemoSeeder.Topics);
        });
        var comments = await db.Comments.Find(_ => true).ToListAsync();
        Assert.Equal(60, comments.Count);
        Assert.All(comments, c => Assert.InRange(c.Body.Length, 1, 2000));
        Assert.Equal(156, await db.Likes.CountDocumentsAsync(_ => true));
        Assert.Equal(120, await db.CommentLikes.CountDocumentsAsync(_ => true));

        await DemoSeeder.RunAsync(db, Size);
        Assert.Equal(20, await db.Users.CountDocumentsAsync(x => x.IsDemo));
        Assert.Equal(60, await db.Comments.CountDocumentsAsync(_ => true));
        Assert.Equal(demo.PasswordHash, (await db.Users.Find(x => x.Id == demo.Id).SingleAsync()).PasswordHash);
    }
}
