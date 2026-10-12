using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Discovery;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Tests;

public sealed class DiscoveryPersistenceTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Feed_snapshot_is_bound_to_its_user_and_mode_and_expires()
    {
        var now = new DateTimeOffset(2026, 1, 20, 12, 0, 0, TimeSpan.Zero);
        var (viewerClient, viewerId) = await factory.SignUpAsync($"snapshot-viewer-{Guid.NewGuid():N}@example.com");
        var (otherClient, otherId) = await factory.SignUpAsync($"snapshot-other-{Guid.NewGuid():N}@example.com");
        using var viewerSession = viewerClient;
        using var otherSession = otherClient;
        var authorA = await factory.QueryAsync(async db => await CreateUserAsync(db, "candidate-a"));
        var authorB = await factory.QueryAsync(async db => await CreateUserAsync(db, "candidate-b"));
        var sourceAuthor = await factory.QueryAsync(async db => await CreateUserAsync(db, "source-author"));

        await factory.QueryAsync(async db =>
        {
            var signal = AddPost(db, sourceAuthor, "Signal", now.AddDays(-2));
            var a = AddPost(db, authorA, "Candidate A", now.AddMinutes(-2));
            var b = AddPost(db, authorB, "Candidate B", now.AddMinutes(-1));
            var topic = $"t-{Guid.NewGuid():N}"[..24];
            db.PostTopics.AddRange(new PostTopic { PostId = signal.Id, Topic = topic },
                new PostTopic { PostId = a.Id, Topic = topic }, new PostTopic { PostId = b.Id, Topic = topic });
            db.Likes.Add(new Like { PostId = signal.Id, UserId = viewerId, CreatedAt = now.AddDays(-1).UtcDateTime });
            await db.SaveChangesAsync();
            return true;
        });

        var clock = new FixedTimeProvider(now);
        var discovery = new DiscoveryService(clock);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var first = await discovery.GetPageAsync(db, viewerId, DiscoveryFeedMode.ForYou, null, 1);
        Assert.Single(first.Items);
        Assert.True(first.HasMore);
        Assert.NotNull(first.NextCursor);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => discovery.GetPageAsync(db, otherId, DiscoveryFeedMode.ForYou, first.NextCursor, 1));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => discovery.GetPageAsync(db, viewerId, DiscoveryFeedMode.FollowingInterests, first.NextCursor, 1));
        var expired = new DiscoveryService(new FixedTimeProvider(now.AddMinutes(31)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => expired.GetPageAsync(db, viewerId, DiscoveryFeedMode.ForYou, first.NextCursor, 1));
    }

    [Fact]
    public async Task Snapshot_page_rechecks_seen_state_after_the_snapshot_was_created()
    {
        var now = new DateTimeOffset(2026, 1, 20, 12, 0, 0, TimeSpan.Zero);
        var (viewerClient, viewerId) = await factory.SignUpAsync($"seen-viewer-{Guid.NewGuid():N}@example.com");
        using var viewerSession = viewerClient;
        var authors = new[]
        {
            await factory.QueryAsync(async db => await CreateUserAsync(db, "seen-candidate-1")),
            await factory.QueryAsync(async db => await CreateUserAsync(db, "seen-candidate-2")),
        };
        await factory.QueryAsync(async db =>
        {
            // A viewer-owned signal cannot also become a feed candidate.
            var signal = AddPost(db, viewerId, "Signal", now.AddDays(-2));
            var first = AddPost(db, authors[0], "First candidate", now.AddMinutes(-1));
            var second = AddPost(db, authors[1], "Second candidate", now);
            var topic = $"t-{Guid.NewGuid():N}"[..24];
            db.PostTopics.AddRange(new PostTopic { PostId = signal.Id, Topic = topic },
                new PostTopic { PostId = first.Id, Topic = topic }, new PostTopic { PostId = second.Id, Topic = topic });
            db.Likes.Add(new Like { PostId = signal.Id, UserId = viewerId, CreatedAt = now.AddDays(-1).UtcDateTime });
            await db.SaveChangesAsync();
            return true;
        });

        var clock = new FixedTimeProvider(now);
        var discovery = new DiscoveryService(clock);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var firstPage = await discovery.GetPageAsync(db, viewerId, DiscoveryFeedMode.ForYou, null, 1);
        Assert.NotNull(firstPage.NextCursor);
        var snapshot = await db.FeedSnapshots.AsNoTracking().SingleAsync(row => row.UserId == viewerId && row.Mode == "for-you");
        var snapshotItems = JsonSerializer.Deserialize<List<DiscoveryItem>>(snapshot.ItemsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var nextPostId = snapshotItems[1].PostId;

        Assert.True(await discovery.MarkSeenAsync(db, viewerId, nextPostId, now.AddMinutes(1).UtcDateTime));
        var nextPage = await discovery.GetPageAsync(db, viewerId, DiscoveryFeedMode.ForYou, firstPage.NextCursor, 1);

        Assert.Empty(nextPage.Items);
        Assert.False(nextPage.HasMore);
    }

    [Fact]
    public async Task Profile_cache_uses_persisted_version_and_rebuilds_after_dirtying()
    {
        var now = new DateTimeOffset(2026, 1, 20, 12, 0, 0, TimeSpan.Zero);
        var viewerId = await factory.QueryAsync(async db => await CreateUserAsync(db, "cache-viewer"));
        var authorId = await factory.QueryAsync(async db => await CreateUserAsync(db, "cache-author"));
        await factory.QueryAsync(async db =>
        {
            var signal = AddPost(db, authorId, "Cache signal", now.AddDays(-1));
            var candidate = AddPost(db, await CreateUserAsync(db, "cache-candidate"), "Cache candidate", now.AddMinutes(-1));
            var topic = $"t-{Guid.NewGuid():N}"[..24];
            db.PostTopics.AddRange(new PostTopic { PostId = signal.Id, Topic = topic }, new PostTopic { PostId = candidate.Id, Topic = topic });
            db.Likes.Add(new Like { UserId = viewerId, PostId = signal.Id, CreatedAt = now.AddHours(-12).UtcDateTime });
            await db.SaveChangesAsync();
            return true;
        });

        var storage = new RecordingDistributedCache();
        var readCache = new OptionalReadCache(storage, new OptionalServiceNamespace("VuePgReplicasAdvBlog", "Test"));
        var discovery = new DiscoveryService(new FixedTimeProvider(now), readCache);
        var digestWindow = new DigestWindow(new DateOnly(2026, 1, 19), now.AddDays(-1), now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();

        // First call builds and persists a profile. The next loads the JSON once and fills Redis;
        // the third validates the unchanged SQL version and reads only the cached dictionary.
        await discovery.GetDigestCandidatesAsync(db, viewerId, digestWindow, now);
        Assert.Equal(0, storage.ProfileSetCount);
        await discovery.GetDigestCandidatesAsync(db, viewerId, digestWindow, now);
        Assert.Equal(1, storage.ProfileSetCount);
        var cachedKey = Assert.Single(storage.ProfileKeys);
        Assert.StartsWith("VuePgReplicasAdvBlog:Test:interest-profile:", cachedKey, StringComparison.Ordinal);
        await discovery.GetDigestCandidatesAsync(db, viewerId, digestWindow, now);
        Assert.Equal(1, storage.ProfileSetCount);

        await discovery.MarkProfileDirtyAsync(db, viewerId, now.AddMinutes(1).UtcDateTime);
        var refreshed = new DiscoveryService(new FixedTimeProvider(now.AddMinutes(2)), readCache);
        var refreshedWindow = new DigestWindow(new DateOnly(2026, 1, 19), now.AddDays(-1), now.AddMinutes(2));
        await refreshed.GetDigestCandidatesAsync(db, viewerId, refreshedWindow, now.AddMinutes(2));
        Assert.Equal(2, storage.ProfileSetCount);
        Assert.NotEqual(cachedKey, storage.ProfileKeys.Last());

        storage.FailReads = true;
        storage.FailWrites = true;
        var duringOutage = await refreshed.GetDigestCandidatesAsync(db, viewerId, refreshedWindow, now.AddMinutes(2));
        Assert.NotNull(duringOutage);
        Assert.False(await db.InterestProfiles.Where(row => row.UserId == viewerId).Select(row => row.Dirty).SingleAsync());
    }

    static Post AddPost(AppDb db, Guid authorId, string title, DateTimeOffset publishedAt)
    {
        var at = publishedAt.UtcDateTime;
        var post = new Post
        {
            AuthorId = authorId,
            Title = title,
            Slug = $"test-{Guid.NewGuid():N}"[..13],
            Body = "A small test body.",
            Excerpt = "A small test body.",
            CreatedAt = at,
            PublishedAt = at,
            UpdatedAt = at
        };
        db.Posts.Add(post);
        db.SaveChanges();
        return post;
    }

    static async Task<Guid> CreateUserAsync(AppDb db, string prefix)
    {
        var user = new User { Email = $"{prefix}-{Guid.NewGuid():N}@example.com", Name = prefix, EmailVerifiedAt = DateTime.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    sealed class RecordingDistributedCache : IDistributedCache
    {
        readonly ConcurrentDictionary<string, byte[]> values = new(StringComparer.Ordinal);
        public List<string> ProfileKeys { get; } = [];
        public int ProfileSetCount => ProfileKeys.Count;
        public bool FailReads { get; set; }
        public bool FailWrites { get; set; }

        public byte[]? Get(string key)
        {
            if (FailReads) throw new IOException("Simulated cache outage.");
            return values.TryGetValue(key, out var value) ? value : null;
        }
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => values.TryRemove(key, out _);
        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            if (FailWrites) throw new IOException("Simulated cache outage.");
            values[key] = value.ToArray();
            if (key.Contains(":interest-profile:", StringComparison.Ordinal)) ProfileKeys.Add(key);
        }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }
}
