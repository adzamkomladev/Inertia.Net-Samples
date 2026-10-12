using System.Net;
using Inertia.Net.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Features.Discovery;
using SvelteMongoBlog.Infrastructure;

namespace SvelteMongoBlog.Tests;

public sealed class OptionalServicesOutageTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Core_http_flows_survive_optional_search_and_cache_outages(bool meilisearchConfigured, bool redisConfigured)
    {
        var searchProbe = new OutageProbe();
        var cacheProbe = new OutageProbe();
        await using var app = new OutageAppFactory(meilisearchConfigured, redisConfigured, searchProbe, cacheProbe);
        using var healthClient = app.Client();
        using var health = await healthClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var email = $"optional-outage-{Guid.NewGuid():N}@yopmail.com";
        var (client, viewerId) = await app.SignUpAsync(email);
        var term = "outage" + Guid.NewGuid().ToString("N");
        using var create = await app.SendAsync(client, "/posts", new { title = term, body = "the searchable body" });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var postId = await app.PostIdAsync(term);
        var (feedAuthorClient, feedAuthorId) = await app.SignUpAsync($"optional-feed-author-{Guid.NewGuid():N}@yopmail.com");
        using var feedAuthor = feedAuthorClient;
        var feedCandidates = await SeedFeedCandidates(app, viewerId, feedAuthorId);
        await MarkAllVisiblePostsIndexed(app);

        using var feed = await app.InertiaGetAsync(client, "/feed?mode=for-you");
        Assert.Equal(HttpStatusCode.OK, feed.StatusCode);
        var feedPage = await feed.GetInertiaPageAsync();
        Assert.Equal(feedCandidates.Archived, feedPage.Prop("posts.data")!.AsArray()[0]!["id"]!.GetValue<Guid>());
        var feedCursor = feedPage.Prop("posts.nextCursor")!.GetValue<string>();

        await app.QueryAsync(async db =>
        {
            await db.Posts.UpdateOneAsync(x => x.Id == feedCandidates.Archived,
                Builders<Post>.Update.Set(x => x.ArchivedAt, DateTime.UtcNow));
            return true;
        });
        using var archiveRecheck = await app.InertiaGetAsync(client, "/feed?mode=for-you");
        Assert.DoesNotContain((await archiveRecheck.GetInertiaPageAsync()).Prop("posts.data")!.AsArray(),
            post => post!["id"]!.GetValue<Guid>() == feedCandidates.Archived);
        await app.QueryAsync(async db =>
        {
            await new DiscoveryService().MarkSeenAsync(db, viewerId, feedCandidates.Seen, DateTime.UtcNow);
            return true;
        });
        using var seenRecheck = await app.InertiaGetAsync(client, $"/feed?mode=for-you&cursor={Uri.EscapeDataString(feedCursor)}");
        Assert.Empty((await seenRecheck.GetInertiaPageAsync()).Prop("posts.data")!.AsArray());

        using var search = await app.InertiaGetAsync(client, $"/posts?q={Uri.EscapeDataString(term)}&engine=meilisearch");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var page = await search.GetInertiaPageAsync();
        Assert.Equal("native", page.Prop("searchEngine")!.GetValue<string>());
        Assert.Contains(postId, page.Prop("posts.data")!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()));
        Assert.Equal(meilisearchConfigured ? 1 : 0, searchProbe.Count);
        Assert.Equal(redisConfigured, cacheProbe.Count > 0);

        if (meilisearchConfigured)
        {
            await app.QueryAsync(async db =>
            {
                var jobs = new JobQueue();
                Assert.True(await SearchIndexing.RequestFullReindexAsync(db, jobs));
                return true;
            });
            using var pendingSearch = await app.InertiaGetAsync(client, $"/posts?q={Uri.EscapeDataString(term)}&engine=meilisearch");
            Assert.Equal(HttpStatusCode.OK, pendingSearch.StatusCode);
            Assert.Equal(1, searchProbe.Count);

            await app.QueryAsync(async db =>
            {
                var job = await db.Jobs.Find(x => x.Type == nameof(ReindexSearchBatch)).FirstAsync();
                await db.Jobs.UpdateOneAsync(x => x.Id == job.Id, Builders<Job>.Update.Set(x => x.Status, JobStatus.Running));
                return true;
            });
            using var runningSearch = await app.InertiaGetAsync(client, $"/posts?q={Uri.EscapeDataString(term)}&engine=meilisearch");
            Assert.Equal(HttpStatusCode.OK, runningSearch.StatusCode);
            Assert.Equal(1, searchProbe.Count);
        }
    }

    static Task<bool> MarkAllVisiblePostsIndexed(AppFactory app) => app.QueryAsync(async db =>
    {
        var posts = await db.Posts.Find(post => post.ArchivedAt == null)
            .Project(post => new { post.Id, post.IndexVersion }).ToListAsync();
        var writes = posts.Select(post => new UpdateOneModel<SearchIndexState>(
            Builders<SearchIndexState>.Filter.Eq(state => state.PostId, post.Id),
            Builders<SearchIndexState>.Update.SetOnInsert(state => state.PostId, post.Id)
                .Set(state => state.RequestedVersion, post.IndexVersion)
                .Set(state => state.AppliedVersion, post.IndexVersion)
                .Set(state => state.TaskId, null).Set(state => state.TaskVersion, null)) { IsUpsert = true }).ToArray();
        if (writes.Length > 0) await db.SearchIndexStates.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false });
        return true;
    });

    static async Task<(Guid Archived, Guid Seen)> SeedFeedCandidates(AppFactory app, Guid viewerId, Guid authorId)
    {
        var ids = await app.QueryAsync(async db =>
        {
            var now = DateTime.UtcNow;
            async Task<Guid> Add(string title, DateTime publishedAt)
            {
                var id = Guid.CreateVersion7();
                await db.Posts.InsertOneAsync(new Post
                {
                    Id = id, AuthorId = authorId, Title = title, Slug = $"{title}-{Guid.NewGuid():N}",
                    Body = "A feed cache test.", Excerpt = title, CreatedAt = publishedAt, UpdatedAt = publishedAt, PublishedAt = publishedAt,
                });
                await db.PostTopics.InsertOneAsync(new PostTopic { PostId = id, Topic = "redis-cache-test" });
                return id;
            }
            var signal = await Add("Profile signal", now.AddDays(-5));
            var archived = await Add("Archive after snapshot", now.AddMinutes(-1));
            _ = await Add("Feed filler 1", now.AddMinutes(-2));
            _ = await Add("Feed filler 2", now.AddMinutes(-3));
            _ = await Add("Feed filler 3", now.AddMinutes(-4));
            _ = await Add("Feed filler 4", now.AddMinutes(-5));
            var seen = await Add("See after snapshot", now.AddMinutes(-6));
            await db.Likes.InsertOneAsync(new Like { UserId = viewerId, PostId = signal, CreatedAt = now.AddHours(-2) });
            await db.Users.UpdateOneAsync(x => x.Id == viewerId, Builders<User>.Update.Set(x => x.Preferences.PostsPerPage, 1));
            return (signal, archived, seen);
        });
        await app.QueryAsync(async db =>
        {
            await new DiscoveryService().MarkSeenAsync(db, viewerId, ids.signal, DateTime.UtcNow.AddHours(-2));
            return true;
        });
        return (ids.archived, ids.seen);
    }

    sealed class OutageAppFactory(bool meilisearchConfigured, bool redisConfigured, OutageProbe searchProbe, OutageProbe cacheProbe) : AppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("SEARCH_DRIVER", meilisearchConfigured ? "meilisearch" : "native");
            builder.UseSetting("MEILISEARCH_URL", meilisearchConfigured ? "http://127.0.0.1:1/" : "");
            builder.UseSetting("CACHE_PROVIDER", redisConfigured ? "redis" : "memory");
            builder.UseSetting("REDIS_URL", redisConfigured ? "redis://unavailable:secret@127.0.0.1:1/7" : "");
            builder.ConfigureServices(services =>
            {
                foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                    && (descriptor.ImplementationType?.Name is "JobWorker" or "ViewEventWriter"
                        || descriptor.ImplementationType?.Name.Contains("Ssr", StringComparison.Ordinal) == true)).ToList())
                    services.Remove(descriptor);
                if (redisConfigured)
                {
                    services.RemoveAll<IDistributedCache>();
                    services.AddSingleton<IDistributedCache>(_ => new ThrowingCache(cacheProbe));
                }
                if (meilisearchConfigured)
                {
                    services.RemoveAll<MeilisearchTaskClient>();
                    services.AddSingleton(new MeilisearchTaskClient(
                        new HttpClient(new UnavailableMeilisearchHandler(searchProbe)),
                        new MeilisearchConnectionSettings(new Uri("http://127.0.0.1:1/"), null)));
                }
            });
        }
    }

    sealed class OutageProbe
    {
        int count;
        public int Count => Volatile.Read(ref count);
        public void Hit() => Interlocked.Increment(ref count);
    }

    sealed class UnavailableMeilisearchHandler(OutageProbe probe) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            probe.Hit();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    sealed class ThrowingCache(OutageProbe probe) : IDistributedCache
    {
        public byte[]? Get(string key) => Fail<byte[]?>();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Fail<Task<byte[]?>>();
        public void Refresh(string key) => Fail<object?>();
        public Task RefreshAsync(string key, CancellationToken token = default) => Fail<Task>();
        public void Remove(string key) => Fail<object?>();
        public Task RemoveAsync(string key, CancellationToken token = default) => Fail<Task>();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Fail<object?>();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Fail<Task>();

        T Fail<T>()
        {
            probe.Hit();
            throw new InvalidOperationException("Simulated Redis outage.");
        }
    }
}
