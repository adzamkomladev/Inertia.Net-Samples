using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Features.Discovery;
using SvelteMongoBlog.Infrastructure;

namespace SvelteMongoBlog.Tests;

public sealed class DiscoverySnapshotTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task First_page_reuses_current_snapshot_and_live_checks_hide_archived_and_seen_posts()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        var (_, viewerId) = await app.SignUpAsync($"discovery-viewer-{Guid.NewGuid():N}@example.test");
        var (_, sourceAuthorId) = await app.SignUpAsync($"discovery-source-{Guid.NewGuid():N}@example.test");
        var (_, candidateAuthorId) = await app.SignUpAsync($"discovery-author-{Guid.NewGuid():N}@example.test");
        var (sourceId, archivedCandidateId, seenCandidateId) = await app.QueryAsync(async db =>
        {
            var source = await AddPostAsync(db, sourceAuthorId, "Profile source", "distributed-systems", now.AddDays(-5));
            var archived = await AddPostAsync(db, candidateAuthorId, "Will be archived", "distributed-systems", now.AddHours(-1));
            var seen = await AddPostAsync(db, candidateAuthorId, "Will be seen", "distributed-systems", now.AddHours(-2));
            await db.Likes.InsertOneAsync(new Like { UserId = viewerId, PostId = source, CreatedAt = now.AddDays(-1).UtcDateTime });
            await new DiscoveryService(clock).MarkSeenAsync(db, viewerId, source, now.AddDays(-1).UtcDateTime);
            return (source, archived, seen);
        });
        var cache = new CountingDistributedCache();
        var discovery = new DiscoveryService(clock, new OptionalReadCache(cache, new OptionalServiceNamespace("SvelteMongoBlog", "Testing")));
        await app.QueryAsync(async db =>
        {
            var first = await discovery.GetPageAsync(db, viewerId, DiscoveryFeedMode.ForYou, null, 1);
            Assert.Equal(archivedCandidateId, Assert.Single(first.Items).PostId);
            Assert.NotNull(first.NextCursor);
            var repeated = await discovery.GetPageAsync(db, viewerId, DiscoveryFeedMode.ForYou, null, 1);
            Assert.Equal(archivedCandidateId, Assert.Single(repeated.Items).PostId);
            Assert.Equal(first.NextCursor, repeated.NextCursor);
            Assert.Equal(1, await db.FeedSnapshots.CountDocumentsAsync(x => x.UserId == viewerId && x.Mode == "for-you"));
            Assert.Contains(cache.Keys, key => key.Contains("interest-profile:", StringComparison.Ordinal));
            Assert.All(cache.Keys, key => Assert.StartsWith("SvelteMongoBlog:Testing:", key));

            await db.Posts.UpdateOneAsync(x => x.Id == archivedCandidateId, Builders<Post>.Update.Set(x => x.ArchivedAt, now.UtcDateTime));
            var hidden = await discovery.GetPageAsync(db, viewerId, DiscoveryFeedMode.ForYou, null, 1);
            Assert.Empty(hidden.Items);
            Assert.Equal(first.NextCursor, hidden.NextCursor);

            await discovery.MarkSeenAsync(db, viewerId, seenCandidateId, now.UtcDateTime);
            var next = await discovery.GetPageAsync(db, viewerId, DiscoveryFeedMode.ForYou, first.NextCursor, 1);
            Assert.Empty(next.Items);
            Assert.False(next.HasMore);

            await discovery.MarkProfileDirtyAsync(db, viewerId, now.UtcDateTime);
            var rebuilt = await discovery.GetPageAsync(db, viewerId, DiscoveryFeedMode.ForYou, null, 1);
            Assert.NotEqual(first.NextCursor?.Split('.')[0], rebuilt.NextCursor?.Split('.')[0]);
            Assert.Equal(2, await db.FeedSnapshots.CountDocumentsAsync(x => x.UserId == viewerId && x.Mode == "for-you"));
            return true;
        });
        Assert.NotEqual(sourceId, archivedCandidateId);
    }

    [Fact]
    public async Task Following_interest_feed_uses_followees_not_the_viewers_personal_profile_and_invalidates_on_unfollow()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var discovery = new DiscoveryService(new FixedTimeProvider(now));
        var (_, viewerId) = await app.SignUpAsync($"following-viewer-{Guid.NewGuid():N}@example.test");
        var (_, followeeId) = await app.SignUpAsync($"following-followee-{Guid.NewGuid():N}@example.test");
        var (_, ownAuthorId) = await app.SignUpAsync($"following-own-author-{Guid.NewGuid():N}@example.test");
        var (_, followAuthorId) = await app.SignUpAsync($"following-follow-author-{Guid.NewGuid():N}@example.test");
        await app.QueryAsync(async db =>
        {
            var ownSignal = await AddPostAsync(db, ownAuthorId, "Own profile signal", "own-topic", now.AddDays(-5));
            var followSignal = await AddPostAsync(db, followAuthorId, "Follow profile signal", "follow-topic", now.AddDays(-4));
            var ownCandidate = await AddPostAsync(db, ownAuthorId, "Own match", "own-topic", now.AddHours(-1));
            var followCandidate = await AddPostAsync(db, followAuthorId, "Follow match", "follow-topic", now.AddMinutes(-30));
            await db.Likes.InsertManyAsync([
                new Like { UserId = viewerId, PostId = ownSignal, CreatedAt = now.AddDays(-1).UtcDateTime },
                new Like { UserId = followeeId, PostId = followSignal, CreatedAt = now.AddDays(-1).UtcDateTime },
            ]);
            await db.Follows.InsertOneAsync(new Follow { FollowerId = viewerId, FolloweeId = followeeId, CreatedAt = now.AddDays(-3).UtcDateTime });
            await discovery.MarkProfileDirtyAsync(db, viewerId, now.UtcDateTime);
            await discovery.MarkProfileDirtyAsync(db, followeeId, now.UtcDateTime);
            await discovery.RefreshProfileAsync(db, viewerId);
            await discovery.RefreshProfileAsync(db, followeeId);

            var personal = await discovery.GetPageAsync(db, viewerId, DiscoveryFeedMode.ForYou, null, 10);
            var following = await discovery.GetPageAsync(db, viewerId, DiscoveryFeedMode.FollowingInterests, null, 10);
            Assert.Contains(personal.Items, item => item.PostId == ownCandidate);
            Assert.DoesNotContain(personal.Items, item => item.PostId == followCandidate);
            Assert.Contains(following.Items, item => item.PostId == followCandidate);
            Assert.DoesNotContain(following.Items, item => item.PostId == ownCandidate);

            await db.Follows.DeleteOneAsync(x => x.FollowerId == viewerId && x.FolloweeId == followeeId);
            var afterUnfollow = await discovery.GetPageAsync(db, viewerId, DiscoveryFeedMode.FollowingInterests, null, 10);
            Assert.Empty(afterUnfollow.Items);
            Assert.Equal(2, await db.FeedSnapshots.CountDocumentsAsync(x => x.UserId == viewerId && x.Mode == "following"));
            return true;
        });
    }

    static async Task<Guid> AddPostAsync(AppDb db, Guid authorId, string title, string topic, DateTimeOffset publishedAt)
    {
        var id = Guid.CreateVersion7();
        var at = publishedAt.UtcDateTime;
        await db.Posts.InsertOneAsync(new Post
        {
            Id = id, AuthorId = authorId, Title = title, Slug = $"{title.ToLowerInvariant().Replace(' ', '-')}-{id}",
            Body = $"Body for {title}", Excerpt = title, CreatedAt = at, UpdatedAt = at, PublishedAt = at,
        });
        await db.PostTopics.InsertOneAsync(new PostTopic { PostId = id, Topic = topic });
        return id;
    }

    sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    sealed class CountingDistributedCache : IDistributedCache
    {
        readonly ConcurrentDictionary<string, byte[]> entries = new(StringComparer.Ordinal);
        public ConcurrentBag<string> Keys { get; } = [];
        public byte[]? Get(string key) { Keys.Add(key); return entries.TryGetValue(key, out var bytes) ? bytes.ToArray() : null; }
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => entries.TryRemove(key, out _);
        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { Keys.Add(key); entries[key] = value.ToArray(); }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        { Set(key, value, options); return Task.CompletedTask; }
    }
}
