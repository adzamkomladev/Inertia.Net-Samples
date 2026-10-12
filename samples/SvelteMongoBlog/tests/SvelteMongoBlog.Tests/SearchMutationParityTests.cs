using System.Net;
using MongoDB.Driver;
using SvelteMongoBlog.Data;

namespace SvelteMongoBlog.Tests;

public sealed class SearchMutationParityTests
{
    [Fact]
    public async Task Concurrent_edits_and_delete_persist_the_actual_search_version_and_no_orphan_topics()
    {
        using var app = new AppFactory(meilisearchEnabled: true);
        var (owner, _) = await app.SignUpAsync($"search-mutation-{Guid.NewGuid():N}@example.test");
        var title = $"Versioned post {Guid.NewGuid():N}";
        using (var created = await app.SendAsync(owner, "/posts", new { title, body = "Initial body.", topics = new[] { "initial" } }))
            AssertRedirect(created);
        var postId = await app.PostIdAsync(title);
        var firstPublishedAt = await app.QueryAsync(db => db.Posts.Find(x => x.Id == postId).Project(x => x.PublishedAt).SingleAsync());

        var updates = await Task.WhenAll(
            app.SendAsync(owner, $"/posts/{postId}", new { title = "Edit one", body = "First edit.", topics = new[] { "first" } }, HttpMethod.Put),
            app.SendAsync(owner, $"/posts/{postId}", new { title = "Edit two", body = "Second edit.", topics = new[] { "second" } }, HttpMethod.Put));
        foreach (var response in updates)
        {
            using (response) AssertRedirect(response);
        }

        var edited = await app.QueryAsync(db => db.Posts.Find(x => x.Id == postId).SingleAsync());
        var editState = await app.QueryAsync(db => db.SearchIndexStates.Find(x => x.PostId == postId).SingleAsync());
        Assert.Equal(3, edited.IndexVersion);
        Assert.Equal(edited.IndexVersion, editState.RequestedVersion);
        Assert.Equal(0, editState.AppliedVersion);
        Assert.Equal(firstPublishedAt, edited.PublishedAt);
        var topics = await app.QueryAsync(db => db.PostTopics.Find(x => x.PostId == postId).Project(x => x.Topic).ToListAsync());
        Assert.Contains(topics.Single(), new[] { "first", "second" });

        using (var deleted = await app.SendAsync(owner, $"/posts/{postId}", new { }, HttpMethod.Delete))
            AssertRedirect(deleted);
        var deleteState = await app.QueryAsync(db => db.SearchIndexStates.Find(x => x.PostId == postId).SingleAsync());
        Assert.Equal(4, deleteState.RequestedVersion);
        Assert.Equal(0, await app.QueryAsync(db => db.PostTopics.CountDocumentsAsync(x => x.PostId == postId)));
        Assert.Equal(0, await app.QueryAsync(db => db.Posts.CountDocumentsAsync(x => x.Id == postId)));
    }

    static void AssertRedirect(HttpResponseMessage response) =>
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther, response.StatusCode.ToString());
}
