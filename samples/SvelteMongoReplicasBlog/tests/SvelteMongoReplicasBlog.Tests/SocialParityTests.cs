using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Features.Discovery;
using SvelteMongoReplicasBlog.Infrastructure;

namespace SvelteMongoReplicasBlog.Tests;

public sealed class SocialParityTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Relationship_actions_are_idempotent_and_archive_is_private_until_restored()
    {
        var (author, authorId) = await app.SignUpAsync($"author-{Guid.NewGuid():N}@example.test");
        using var created = await app.SendAsync(author, "/posts", new { title = $"Archive check {Guid.NewGuid():N}", body = "A post.", topics = new[] { "mongodb" } });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var slug = created.Headers.Location!.OriginalString.Split('/').Last();
        var postId = await app.QueryAsync(db => db.Posts.Find(x => x.Slug == slug).Project(x => x.Id).SingleAsync());

        var (reader, readerId) = await app.SignUpAsync($"reader-{Guid.NewGuid():N}@example.test");
        using (var follow1 = await app.SendAsync(reader, $"/users/{authorId}/follow", new { }, HttpMethod.Put)) AssertRedirect(follow1);
        using (var follow2 = await app.SendAsync(reader, $"/users/{authorId}/follow", new { }, HttpMethod.Put)) AssertRedirect(follow2);
        using (var bookmark1 = await app.SendAsync(reader, $"/posts/{postId}/bookmark", new { }, HttpMethod.Put)) AssertRedirect(bookmark1);
        using (var bookmark2 = await app.SendAsync(reader, $"/posts/{postId}/bookmark", new { }, HttpMethod.Put)) AssertRedirect(bookmark2);
        using (var repost1 = await app.SendAsync(reader, $"/posts/{postId}/repost", new { }, HttpMethod.Put)) AssertRedirect(repost1);
        using (var repost2 = await app.SendAsync(reader, $"/posts/{postId}/repost", new { }, HttpMethod.Put)) AssertRedirect(repost2);
        using (var comment = await app.SendAsync(reader, $"/posts/{postId}/comments", new { body = "A useful comment." })) AssertRedirect(comment);
        var commentId = await app.QueryAsync(db => db.Comments.Find(x => x.PostId == postId).Project(x => x.Id).SingleAsync());
        using (var commentLike1 = await app.SendAsync(reader, $"/comments/{commentId}/like", new { }, HttpMethod.Put)) AssertRedirect(commentLike1);
        using (var commentLike2 = await app.SendAsync(reader, $"/comments/{commentId}/like", new { }, HttpMethod.Put)) AssertRedirect(commentLike2);

        Assert.Equal(1, await app.QueryAsync(db => db.Follows.CountDocumentsAsync(x => x.FollowerId == readerId && x.FolloweeId == authorId)));
        Assert.Equal(1, await app.QueryAsync(db => db.Bookmarks.CountDocumentsAsync(x => x.UserId == readerId && x.PostId == postId)));
        Assert.Equal(1, await app.QueryAsync(db => db.Reposts.CountDocumentsAsync(x => x.UserId == readerId && x.PostId == postId)));
        Assert.Equal(1, await app.QueryAsync(db => db.CommentLikes.CountDocumentsAsync(x => x.UserId == readerId && x.CommentId == commentId)));
        Assert.Equal(1, await app.QueryAsync(db => db.SeenPosts.CountDocumentsAsync(x => x.UserId == readerId && x.PostId == postId)));

        using (var archive = await app.SendAsync(author, $"/posts/{postId}/archive", new { }, HttpMethod.Put)) AssertRedirect(archive);
        using (var duplicateArchive = await app.SendAsync(author, $"/posts/{postId}/archive", new { }, HttpMethod.Put)) AssertRedirect(duplicateArchive);
        using (var ownerPage = await app.InertiaGetAsync(author, $"/posts/{slug}")) Assert.Equal(HttpStatusCode.OK, ownerPage.StatusCode);
        using (var publicPage = await app.InertiaGetAsync(app.Client(), $"/posts/{slug}")) Assert.Equal(HttpStatusCode.NotFound, publicPage.StatusCode);
        using (var restore = await app.SendAsync(author, $"/posts/{postId}/archive", new { }, HttpMethod.Delete)) AssertRedirect(restore);
        using (var duplicateRestore = await app.SendAsync(author, $"/posts/{postId}/archive", new { }, HttpMethod.Delete)) AssertRedirect(duplicateRestore);
        using (var publicAfterRestore = await app.InertiaGetAsync(app.Client(), $"/posts/{slug}")) Assert.Equal(HttpStatusCode.OK, publicAfterRestore.StatusCode);
        Assert.Equal(1, await app.QueryAsync(db => db.Posts.Find(x => x.Id == postId).Project(x => x.CommentCount).SingleAsync()));
        Assert.Equal(2, await app.QueryAsync(db => db.Posts.Find(x => x.Id == postId).Project(x => x.IndexVersion).SingleAsync()));
    }

    [Fact]
    public async Task Archive_is_available_to_any_authenticated_owner_and_follow_changes_evict_only_following_snapshots()
    {
        var (reader, readerId) = await app.SignUpAsync($"snapshot-reader-{Guid.NewGuid():N}@example.test");
        var (_, followeeId) = await app.SignUpAsync($"snapshot-followee-{Guid.NewGuid():N}@example.test");
        var (author, _) = await app.SignUpAsync($"archive-author-{Guid.NewGuid():N}@example.test");
        using var created = await app.SendAsync(author, "/posts", new { title = $"Transfer post {Guid.NewGuid():N}", body = "A post." });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var slug = created.Headers.Location!.OriginalString.Split('/').Last();
        var postId = await app.QueryAsync(db => db.Posts.Find(x => x.Slug == slug).Project(x => x.Id).SingleAsync());
        var (unverifiedOwner, unverifiedOwnerId) = await app.SignUpAsync($"unverified-owner-{Guid.NewGuid():N}@example.test", verified: false);

        await app.QueryAsync(async db =>
        {
            await db.Posts.UpdateOneAsync(x => x.Id == postId, Builders<Post>.Update.Set(x => x.AuthorId, unverifiedOwnerId));
            return true;
        });
        using (var archive = await app.SendAsync(unverifiedOwner, $"/posts/{postId}/archive", new { }, HttpMethod.Put)) AssertRedirect(archive);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var discovery = scope.ServiceProvider.GetRequiredService<DiscoveryService>();
            await discovery.GetPageAsync(db, readerId, DiscoveryFeedMode.ForYou, null, 10);
            await discovery.GetPageAsync(db, readerId, DiscoveryFeedMode.FollowingInterests, null, 10);
            Assert.Equal(1, await db.FeedSnapshots.CountDocumentsAsync(x => x.UserId == readerId && x.Mode == "for-you"));
            Assert.Equal(1, await db.FeedSnapshots.CountDocumentsAsync(x => x.UserId == readerId && x.Mode == "following"));
        }

        using (var follow = await app.SendAsync(reader, $"/users/{followeeId}/follow", new { }, HttpMethod.Put)) AssertRedirect(follow);
        Assert.Equal(0, await app.QueryAsync(db => db.FeedSnapshots.CountDocumentsAsync(x => x.UserId == readerId && x.Mode == "following")));
        Assert.Equal(1, await app.QueryAsync(db => db.FeedSnapshots.CountDocumentsAsync(x => x.UserId == readerId && x.Mode == "for-you")));

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var discovery = scope.ServiceProvider.GetRequiredService<DiscoveryService>();
            await discovery.GetPageAsync(db, readerId, DiscoveryFeedMode.FollowingInterests, null, 10);
            Assert.Equal(1, await db.FeedSnapshots.CountDocumentsAsync(x => x.UserId == readerId && x.Mode == "following"));
        }
        using (var unfollow = await app.SendAsync(reader, $"/users/{followeeId}/follow", new { }, HttpMethod.Delete)) AssertRedirect(unfollow);
        Assert.Equal(0, await app.QueryAsync(db => db.FeedSnapshots.CountDocumentsAsync(x => x.UserId == readerId && x.Mode == "following")));
        Assert.Equal(1, await app.QueryAsync(db => db.FeedSnapshots.CountDocumentsAsync(x => x.UserId == readerId && x.Mode == "for-you")));
    }

    [Fact]
    public async Task Concurrent_social_edge_puts_and_deletes_leave_at_most_one_row_per_edge()
    {
        var (author, authorId) = await app.SignUpAsync($"concurrent-author-{Guid.NewGuid():N}@example.test");
        var title = $"Concurrent edge {Guid.NewGuid():N}";
        using (await app.SendAsync(author, "/posts", new { title, body = "A post for concurrent edges." })) { }
        var postId = await app.PostIdAsync(title);
        var (reader, readerId) = await app.SignUpAsync($"concurrent-reader-{Guid.NewGuid():N}@example.test");
        using (await app.SendAsync(reader, $"/posts/{postId}/comments", new { body = "A comment for concurrent likes." })) { }
        var commentId = await app.QueryAsync(db => db.Comments.Find(x => x.PostId == postId && x.UserId == readerId).Project(x => x.Id).SingleAsync());

        var putRequests = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 2; i++)
        {
            putRequests.Add(app.SendAsync(reader, $"/users/{authorId}/follow", new { }, HttpMethod.Put));
            putRequests.Add(app.SendAsync(reader, $"/posts/{postId}/bookmark", new { }, HttpMethod.Put));
            putRequests.Add(app.SendAsync(reader, $"/posts/{postId}/repost", new { }, HttpMethod.Put));
            putRequests.Add(app.SendAsync(reader, $"/comments/{commentId}/like", new { }, HttpMethod.Put));
        }
        var putResponses = await Task.WhenAll(putRequests);
        foreach (var response in putResponses) { using (response) AssertRedirect(response); }

        Assert.Equal(1, await app.QueryAsync(db => db.Follows.CountDocumentsAsync(x => x.FollowerId == readerId && x.FolloweeId == authorId)));
        Assert.Equal(1, await app.QueryAsync(db => db.Bookmarks.CountDocumentsAsync(x => x.UserId == readerId && x.PostId == postId)));
        Assert.Equal(1, await app.QueryAsync(db => db.Reposts.CountDocumentsAsync(x => x.UserId == readerId && x.PostId == postId)));
        Assert.Equal(1, await app.QueryAsync(db => db.CommentLikes.CountDocumentsAsync(x => x.UserId == readerId && x.CommentId == commentId)));

        var deleteRequests = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 2; i++)
        {
            deleteRequests.Add(app.SendAsync(reader, $"/users/{authorId}/follow", new { }, HttpMethod.Delete));
            deleteRequests.Add(app.SendAsync(reader, $"/posts/{postId}/bookmark", new { }, HttpMethod.Delete));
            deleteRequests.Add(app.SendAsync(reader, $"/posts/{postId}/repost", new { }, HttpMethod.Delete));
            deleteRequests.Add(app.SendAsync(reader, $"/comments/{commentId}/like", new { }, HttpMethod.Delete));
        }
        var deleteResponses = await Task.WhenAll(deleteRequests);
        foreach (var response in deleteResponses) { using (response) AssertRedirect(response); }

        Assert.Equal(0, await app.QueryAsync(db => db.Follows.CountDocumentsAsync(x => x.FollowerId == readerId && x.FolloweeId == authorId)));
        Assert.Equal(0, await app.QueryAsync(db => db.Bookmarks.CountDocumentsAsync(x => x.UserId == readerId && x.PostId == postId)));
        Assert.Equal(0, await app.QueryAsync(db => db.Reposts.CountDocumentsAsync(x => x.UserId == readerId && x.PostId == postId)));
        Assert.Equal(0, await app.QueryAsync(db => db.CommentLikes.CountDocumentsAsync(x => x.UserId == readerId && x.CommentId == commentId)));
    }

    [Fact]
    public async Task Feed_hydration_and_social_edge_lookup_indexes_are_installed()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var topicIndexes = await IndexNames(db.PostTopics);
        var followIndexes = await IndexNames(db.Follows);
        var bookmarkIndexes = await IndexNames(db.Bookmarks);
        var commentLikeIndexes = await IndexNames(db.CommentLikes);
        var seenIndexes = await IndexNames(db.SeenPosts);

        Assert.Contains("post_topic_unique", topicIndexes);
        Assert.Contains("followees_by_follower", followIndexes);
        Assert.Contains("user_post_unique", bookmarkIndexes);
        Assert.Contains("comment_user_unique", commentLikeIndexes);
        Assert.Contains("user_post_unique", seenIndexes);

        static async Task<HashSet<string>> IndexNames<T>(IMongoCollection<T> collection)
        {
            using var cursor = await collection.Indexes.ListAsync();
            return (await cursor.ToListAsync()).Select(index => index["name"].AsString).ToHashSet(StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task Digest_is_unique_per_local_day_and_uses_only_unseen_matches()
    {
        var now = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(12), TimeSpan.Zero);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var clock = new DigestClock(new FixedTimeProvider(now));
        var (author, authorId) = await app.SignUpAsync($"digest-author-{Guid.NewGuid():N}@example.test");
        var (reader, readerId) = await app.SignUpAsync($"digest-reader-{Guid.NewGuid():N}@example.test");
        var sourceTitle = $"Technology signal {Guid.NewGuid():N}";
        var candidateTitle = $"Technology yesterday {Guid.NewGuid():N}";
        using (await app.SendAsync(author, "/posts", new { title = sourceTitle, body = "A signal post.", topics = new[] { "technology" } })) { }
        using (await app.SendAsync(author, "/posts", new { title = candidateTitle, body = "A recommendation.", topics = new[] { "technology" } })) { }
        var sourceId = await app.PostIdAsync(sourceTitle);
        var candidateId = await app.PostIdAsync(candidateTitle);
        await app.QueryAsync(async db =>
        {
            await db.Posts.UpdateOneAsync(x => x.Id == candidateId, Builders<Post>.Update.Set(x => x.PublishedAt, now.UtcDateTime.AddDays(-1).AddHours(3)));
            await db.Likes.InsertOneAsync(new Like { UserId = readerId, PostId = sourceId, CreatedAt = now.UtcDateTime.AddMinutes(-10) });
            await db.Users.UpdateOneAsync(x => x.Id == readerId, Builders<User>.Update.Set(x => x.Preferences.DailyRecommendationDigest, true).Set(x => x.Preferences.TimeZoneId, "UTC").Set(x => x.NextDigestAtUtc, now.UtcDateTime.AddHours(-1)));
            return true;
        });

        await using var scope = app.Services.CreateAsyncScope();
        var discovery = new DiscoveryService(new FixedTimeProvider(now));
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        Assert.Equal(1, await db.Likes.CountDocumentsAsync(x => x.UserId == readerId && x.PostId == sourceId));
        Assert.Equal(1, await db.PostTopics.CountDocumentsAsync(x => x.PostId == sourceId && x.Topic == "technology"));
        await discovery.MarkProfileDirtyAsync(db, readerId, now.UtcDateTime);
        await discovery.RefreshProfileAsync(db, readerId);
        Assert.Equal(1, await db.PostTopics.CountDocumentsAsync(x => x.PostId == candidateId && x.Topic == "technology"));
        var profile = await db.InterestProfiles.Find(x => x.UserId == readerId).SingleAsync();
        Assert.Contains("technology", profile.TopicsJson);
        var window = new DigestWindow(today.AddDays(-1), DigestClock.GetLocalDayBoundaryUtc(today.AddDays(-1), TimeZoneInfo.Utc), DigestClock.GetLocalDayBoundaryUtc(today, TimeZoneInfo.Utc));
        Assert.Contains((await discovery.GetDigestCandidatesAsync(db, readerId, window, now)).Select(x => x.PostId), id => id == candidateId);
        await discovery.MarkSeenAsync(db, readerId, candidateId, now.UtcDateTime);
        Assert.DoesNotContain((await discovery.GetDigestCandidatesAsync(db, readerId, window, now)).Select(x => x.PostId), id => id == candidateId);
        await discovery.MarkSeenAsync(db, readerId, candidateId, now.UtcDateTime.AddMinutes(-1));
        await db.SeenPosts.DeleteOneAsync(x => x.UserId == readerId && x.PostId == candidateId);

        var digest = new DigestService(discovery, clock, scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
        var jobs = scope.ServiceProvider.GetRequiredService<JobQueue>();
        Assert.Equal(1, await digest.EnqueueDueAsync(db, jobs));
        Assert.Equal(0, await digest.EnqueueDueAsync(db, jobs));
        var delivery = await db.DigestDeliveries.Find(x => x.UserId == readerId).SingleAsync();
        Assert.Equal(today, delivery.LocalDate);
        Assert.Equal(DigestClock.GetLocalDayBoundaryUtc(today.AddDays(1), TimeZoneInfo.Utc).UtcDateTime, delivery.ExpiresAtUtc);
        Assert.Equal(1, await db.DigestDeliveries.CountDocumentsAsync(x => x.UserId == readerId && x.LocalDate == delivery.LocalDate));
    }

    sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    static void AssertRedirect(HttpResponseMessage response) => Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther, $"Expected redirect, got {(int)response.StatusCode}.");
}
