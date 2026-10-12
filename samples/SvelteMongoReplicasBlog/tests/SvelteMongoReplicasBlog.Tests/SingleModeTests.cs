using System.Net;
using MongoDB.Driver;

namespace SvelteMongoReplicasBlog.Tests;

/// Boots the app in the default single mode (no transactions) against the shared replica-set test server.
public sealed class SingleModeAppFactory : AppFactory
{
    protected override string MongoMode => "single";
}

public sealed class SingleModeTests(SingleModeAppFactory app) : IClassFixture<SingleModeAppFactory>
{
    [Fact]
    public async Task Register_post_and_comment_flows_work_without_transactions()
    {
        var (client, _) = await app.SignUpAsync("single-mode@example.test");

        using var created = await app.SendAsync(client, "/posts", new
        {
            title = "Single mode post",
            body = "Writes run without transactions.",
            topics = Array.Empty<string>(),
            coverAssetId = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var postId = await app.PostIdAsync("Single mode post");
        using var commented = await app.SendAsync(client, $"/posts/{postId}/comments", new { body = "Works without transactions." });
        Assert.Equal(HttpStatusCode.Redirect, commented.StatusCode);

        var comments = await app.QueryAsync(db => db.Comments.Find(x => x.PostId == postId).ToListAsync());
        Assert.Single(comments);
    }
}
