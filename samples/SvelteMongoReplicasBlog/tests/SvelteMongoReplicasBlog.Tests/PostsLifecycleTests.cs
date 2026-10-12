using System.Net;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;

namespace SvelteMongoReplicasBlog.Tests;

public sealed class PostsLifecycleTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Liking_a_post_records_it_as_seen_without_a_view_beacon()
    {
        var (author, _) = await app.SignUpAsync($"like-seen-author-{Guid.NewGuid():N}@example.test");
        var (reader, readerId) = await app.SignUpAsync($"like-seen-reader-{Guid.NewGuid():N}@example.test");
        var title = $"Like seen {Guid.NewGuid():N}";
        using (var created = await app.SendAsync(author, "/posts", new { title, body = "Like me." }))
            AssertRedirect(created);
        var postId = await app.PostIdAsync(title);
        using (var like = await app.SendAsync(reader, $"/posts/{postId}/like", new { }))
            AssertRedirect(like);
        Assert.Equal(1, await app.QueryAsync(db => db.SeenPosts.CountDocumentsAsync(x => x.UserId == readerId && x.PostId == postId)));
    }

    [Fact]
    public async Task Deleting_a_comment_cascades_comment_likes_in_the_same_post_revision()
    {
        var (author, _) = await app.SignUpAsync($"comment-delete-author-{Guid.NewGuid():N}@example.test");
        var (reader, _) = await app.SignUpAsync($"comment-delete-reader-{Guid.NewGuid():N}@example.test");
        var title = $"Comment delete {Guid.NewGuid():N}";
        using (var created = await app.SendAsync(author, "/posts", new { title, body = "Comment me." }))
            AssertRedirect(created);
        var postId = await app.PostIdAsync(title);
        using (var comment = await app.SendAsync(reader, $"/posts/{postId}/comments", new { body = "Delete me." }))
            AssertRedirect(comment);
        var commentId = await app.QueryAsync(db => db.Comments.Find(x => x.PostId == postId).Project(x => x.Id).SingleAsync());
        using (var like = await app.SendAsync(author, $"/comments/{commentId}/like", new { }, HttpMethod.Put))
            AssertRedirect(like);
        var revisionBefore = await app.QueryAsync(db => db.Posts.Find(x => x.Id == postId).Project(x => x.SocialRevision).SingleAsync());

        using (var deleted = await app.SendAsync(reader, $"/comments/{commentId}", new { }, HttpMethod.Delete))
            AssertRedirect(deleted);
        Assert.Equal(0, await app.QueryAsync(db => db.CommentLikes.CountDocumentsAsync(x => x.CommentId == commentId)));
        Assert.Equal(0, await app.QueryAsync(db => db.Comments.CountDocumentsAsync(x => x.Id == commentId)));
        Assert.True(await app.QueryAsync(db => db.Posts.Find(x => x.Id == postId).Project(x => x.SocialRevision).SingleAsync()) > revisionBefore);
        Assert.Equal(0, await app.QueryAsync(db => db.Posts.Find(x => x.Id == postId).Project(x => x.CommentCount).SingleAsync()));
    }

    [Fact]
    public async Task View_beacon_persists_seen_state_and_post_delete_cascades_relationships()
    {
        var (author, authorId) = await app.SignUpAsync($"lifecycle-author-{Guid.NewGuid():N}@example.test");
        var (reader, readerId) = await app.SignUpAsync($"lifecycle-reader-{Guid.NewGuid():N}@example.test");
        Assert.NotEqual(authorId, readerId);
        var title = $"Lifecycle {Guid.NewGuid():N}";
        using (var created = await app.SendAsync(author, "/posts", new { title, body = "Lifecycle body", topics = new[] { "lifecycle" } }))
            AssertRedirect(created);
        var postId = await app.PostIdAsync(title);

        using (var view = await app.SendAsync(reader, $"/posts/{postId}/view", new { }))
            Assert.Equal(HttpStatusCode.NoContent, view.StatusCode);
        Assert.Equal(1, await app.QueryAsync(db => db.SeenPosts.CountDocumentsAsync(x => x.UserId == readerId && x.PostId == postId)));
        Assert.True(await app.QueryAsync(db => db.Posts.Find(x => x.Id == postId).Project(x => x.SocialRevision).SingleAsync()) > 0);

        using (var bookmark = await app.SendAsync(reader, $"/posts/{postId}/bookmark", new { }, HttpMethod.Put))
            AssertRedirect(bookmark);
        using (var repost = await app.SendAsync(reader, $"/posts/{postId}/repost", new { }, HttpMethod.Put))
            AssertRedirect(repost);
        using (var like = await app.SendAsync(reader, $"/posts/{postId}/like", new { }))
            AssertRedirect(like);
        using (var comment = await app.SendAsync(reader, $"/posts/{postId}/comments", new { body = "A lifecycle comment." }))
            AssertRedirect(comment);
        var commentId = await app.QueryAsync(db => db.Comments.Find(x => x.PostId == postId).Project(x => x.Id).SingleAsync());
        using (var commentLike = await app.SendAsync(reader, $"/comments/{commentId}/like", new { }, HttpMethod.Put))
            AssertRedirect(commentLike);

        using (var archive = await app.SendAsync(author, $"/posts/{postId}/archive", new { }, HttpMethod.Put))
            AssertRedirect(archive);
        using (var archivedView = await app.SendAsync(reader, $"/posts/{postId}/view", new { }))
            Assert.Equal(HttpStatusCode.NotFound, archivedView.StatusCode);
        Assert.Equal(1, await app.QueryAsync(db => db.SeenPosts.CountDocumentsAsync(x => x.UserId == readerId && x.PostId == postId)));
        using (var restore = await app.SendAsync(author, $"/posts/{postId}/archive", new { }, HttpMethod.Delete))
            AssertRedirect(restore);

        using (var deleted = await app.SendAsync(author, $"/posts/{postId}", new { }, HttpMethod.Delete))
            AssertRedirect(deleted);
        Assert.Equal(0, await app.QueryAsync(db => db.Posts.CountDocumentsAsync(x => x.Id == postId)));
        Assert.Equal(0, await app.QueryAsync(db => db.PostTopics.CountDocumentsAsync(x => x.PostId == postId)));
        Assert.Equal(0, await app.QueryAsync(db => db.Comments.CountDocumentsAsync(x => x.PostId == postId)));
        Assert.Equal(0, await app.QueryAsync(db => db.CommentLikes.CountDocumentsAsync(x => x.CommentId == commentId)));
        Assert.Equal(0, await app.QueryAsync(db => db.Likes.CountDocumentsAsync(x => x.PostId == postId)));
        Assert.Equal(0, await app.QueryAsync(db => db.Bookmarks.CountDocumentsAsync(x => x.PostId == postId)));
        Assert.Equal(0, await app.QueryAsync(db => db.Reposts.CountDocumentsAsync(x => x.PostId == postId)));
        Assert.Equal(0, await app.QueryAsync(db => db.SeenPosts.CountDocumentsAsync(x => x.PostId == postId)));
    }

    static void AssertRedirect(HttpResponseMessage response) =>
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther, response.StatusCode.ToString());
}
