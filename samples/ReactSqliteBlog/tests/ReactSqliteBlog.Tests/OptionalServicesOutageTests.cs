using System.Net;
using Inertia.Net.Testing;
using ReactSqliteBlog.Data;
using ReactSqliteBlog.Infrastructure;
using ReactSqliteBlog.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ReactSqliteBlog.Tests;

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
        using var factory = new BlogFactory(builder =>
        {
            builder.UseSetting("SEARCH_DRIVER", meilisearchConfigured ? "meilisearch" : "native");
            builder.UseSetting("MEILISEARCH_URL", meilisearchConfigured ? "http://127.0.0.1:1/" : "");
            builder.UseSetting("REDIS_URL", redisConfigured ? "redis://unavailable:secret@127.0.0.1:1/7" : "");
            builder.ConfigureServices(services =>
            {
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
        });

        using var healthClient = factory.CreateClient();
        using var health = await healthClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var email = BlogFactory.NewEmail("optional-outage");
        await factory.CreateUserAsync(email);
        var client = factory.NewClient();
        await client.LoginOkAsync(email);
        var term = "outage" + Guid.NewGuid().ToString("N");
        var postId = await client.CreatePostAsync(term, "the searchable body");
        MarkAllVisiblePostsIndexed(factory);

        using var feed = await client.GetAsync("/feed?mode=for-you");
        Assert.Equal(HttpStatusCode.OK, feed.StatusCode);

        using var search = await client.GetAsync($"/posts?q={Uri.EscapeDataString(term)}&engine=meilisearch");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var page = await search.GetInertiaPageAsync();
        Assert.Equal("native", page.Prop("searchEngine")!.GetValue<string>());
        Assert.Contains(postId, page.Prop("posts.data")!.AsArray().Select(item => Guid.Parse(item!["id"]!.GetValue<string>())));
        Assert.Equal(meilisearchConfigured ? 1 : 0, searchProbe.Count);
        Assert.Equal(redisConfigured, cacheProbe.Count > 0);

        if (meilisearchConfigured)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var jobs = scope.ServiceProvider.GetRequiredService<JobQueue>();
            Assert.True(await SearchIndexing.RequestFullReindexAsync(db, jobs));
            using var pendingSearch = await client.GetAsync($"/posts?q={Uri.EscapeDataString(term)}&engine=meilisearch");
            Assert.Equal(HttpStatusCode.OK, pendingSearch.StatusCode);
            Assert.Equal(1, searchProbe.Count);

            var reindex = await db.Jobs.SingleAsync(job => job.Type == nameof(ReindexSearchBatch));
            reindex.Status = JobStatus.Running;
            await db.SaveChangesAsync();
            using var runningSearch = await client.GetAsync($"/posts?q={Uri.EscapeDataString(term)}&engine=meilisearch");
            Assert.Equal(HttpStatusCode.OK, runningSearch.StatusCode);
            Assert.Equal(1, searchProbe.Count);
        }
    }

    static void MarkAllVisiblePostsIndexed(BlogFactory factory) => factory.WithDb(db =>
    {
        var posts = db.Posts.IgnoreQueryFilters().AsNoTracking()
            .Where(post => post.ArchivedAt == null && post.IsPublished)
            .Select(post => new { post.Id, post.IndexVersion }).ToList();
        var states = db.SearchIndexStates.ToDictionary(state => state.PostId);
        foreach (var post in posts)
        {
            if (!states.TryGetValue(post.Id, out var state))
            {
                state = new SearchIndexState { PostId = post.Id };
                db.SearchIndexStates.Add(state);
            }
            state.RequestedVersion = post.IndexVersion;
            state.AppliedVersion = post.IndexVersion;
            state.TaskId = null;
            state.TaskVersion = null;
        }
        db.SaveChanges();
        return true;
    });

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
