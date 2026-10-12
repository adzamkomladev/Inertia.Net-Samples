using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Infrastructure;

namespace SvelteMongoBlog.Tests;

public sealed class SearchIndexingDurabilityTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Setup_task_is_persisted_and_resumed_after_a_worker_restart()
    {
        await app.QueryAsync(async db =>
        {
            await db.SearchIndexStates.DeleteManyAsync(_ => true);
            return true;
        });
        var now = DateTime.UtcNow;
        var author = await CreateUserAsync("search-setup");
        var postId = await app.QueryAsync(async db =>
        {
            var id = Guid.CreateVersion7();
            await db.Posts.InsertOneAsync(new Post
            {
                Id = id, AuthorId = author, Title = "Indexed", Slug = $"indexed-{id}", Body = "A test document.",
                Excerpt = "A test", CreatedAt = now, UpdatedAt = now, PublishedAt = now, IndexVersion = 1,
            });
            await db.SearchIndexStates.InsertOneAsync(new SearchIndexState { PostId = id, RequestedVersion = 1 });
            return id;
        });
        var handler = new FakeMeiliHandler(failFirstTaskPoll: true);
        var client = NewMeiliClient(handler);
        var names = new OptionalServiceNamespace("SvelteMongoBlog", "Testing");

        await Assert.ThrowsAsync<HttpRequestException>(() => app.QueryAsync(async db =>
        {
            await SearchIndexing.SyncAsync(db, client, names, postId);
            return true;
        }));
        var interrupted = await app.QueryAsync(db => db.SearchIndexStates.Find(x => x.PostId == Guid.Empty).FirstAsync());
        Assert.Equal(1, interrupted.TaskVersion);
        Assert.Equal(1, interrupted.TaskId);

        await app.QueryAsync(async db =>
        {
            await SearchIndexing.SyncAsync(db, client, names, postId);
            return true;
        });
        var setup = await app.QueryAsync(db => db.SearchIndexStates.Find(x => x.PostId == Guid.Empty).FirstAsync());
        var indexed = await app.QueryAsync(db => db.SearchIndexStates.Find(x => x.PostId == postId).FirstAsync());
        Assert.Equal(2, setup.AppliedVersion);
        Assert.Null(setup.TaskId);
        Assert.Equal(1, indexed.AppliedVersion);
        Assert.Null(indexed.TaskId);
        Assert.Equal(1, handler.IndexCreateRequests);
        Assert.Equal(1, handler.SettingsRequests);
        Assert.Equal(1, handler.UpsertRequests);
    }

    [Fact]
    public async Task Reindex_processes_at_most_500_posts_in_bulk_and_persists_a_continuation()
    {
        // The class is order-independent only when the batch scan sees exactly these 501 posts.
        await app.QueryAsync(async db =>
        {
            await db.SearchIndexStates.DeleteManyAsync(_ => true);
            await db.Posts.DeleteManyAsync(_ => true);
            return true;
        });
        var now = DateTime.UtcNow;
        var author = await CreateUserAsync("search-bulk");
        var ids = AppFactory.OrderedIds(501);
        var posts = Enumerable.Range(0, 501).Select(i => new Post
        {
            Id = ids[i], AuthorId = author, Title = $"Bulk {i}", Slug = $"bulk-{ids[i]}", Body = "Bulk body",
            Excerpt = "Bulk", CreatedAt = now.AddMinutes(-i), UpdatedAt = now.AddMinutes(-i), PublishedAt = now.AddMinutes(-i),
            ArchivedAt = i == 0 ? now : null, IndexVersion = 1,
        }).ToArray();
        await app.QueryAsync(async db => { await db.Posts.InsertManyAsync(posts); return true; });
        var handler = new FakeMeiliHandler();
        var client = NewMeiliClient(handler);
        var names = new OptionalServiceNamespace("SvelteMongoBlog", "Testing");

        await app.QueryAsync(async db =>
        {
            await SearchIndexing.ProcessReindexBatchAsync(db, new JobQueue(), client, names, Guid.Empty);
            return true;
        });

        Assert.Equal(1, handler.UpsertRequests);
        Assert.Equal(1, handler.DeleteBatchRequests);
        Assert.Equal(499, handler.LastUpsertDocumentCount);
        var indexed = await app.QueryAsync(db => db.SearchIndexStates.Find(x => ids.Take(500).Contains(x.PostId)).ToListAsync());
        Assert.Equal(500, indexed.Count);
        Assert.All(indexed, state => Assert.Equal(1, state.AppliedVersion));
        var continuationPayload = JsonSerializer.Serialize(new ReindexSearchBatch(posts[499].Id));
        var continuation = await app.QueryAsync(db => db.Jobs.Find(x => x.Type == nameof(ReindexSearchBatch)
            && x.Payload == continuationPayload).FirstOrDefaultAsync());
        Assert.NotNull(continuation);
    }

    [Fact]
    public async Task Full_reindex_request_coalesces_while_another_batch_is_active()
    {
        await app.QueryAsync(async db =>
        {
            await db.Jobs.DeleteManyAsync(x => x.Type == nameof(ReindexSearchBatch));
            var jobs = new JobQueue();
            Assert.True(await SearchIndexing.RequestFullReindexAsync(db, jobs));
            Assert.False(await SearchIndexing.RequestFullReindexAsync(db, jobs));
            return true;
        });
    }

    static MeilisearchTaskClient NewMeiliClient(FakeMeiliHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://meili.test/"), Timeout = TimeSpan.FromSeconds(10) };
        return new MeilisearchTaskClient(http, new MeilisearchConnectionSettings(new Uri("http://meili.test/"), null));
    }

    async Task<Guid> CreateUserAsync(string prefix) => await app.QueryAsync(async db =>
    {
        var id = Guid.CreateVersion7();
        await db.Users.InsertOneAsync(new User { Id = id, Email = $"{prefix}-{Guid.NewGuid():N}@example.test", Name = prefix });
        return id;
    });

    sealed class FakeMeiliHandler(bool failFirstTaskPoll = false) : HttpMessageHandler
    {
        long taskId;
        int failPoll = failFirstTaskPoll ? 1 : 0;
        bool indexCreated;
        public int IndexCreateRequests { get; private set; }
        public int SettingsRequests { get; private set; }
        public int UpsertRequests { get; private set; }
        public int DeleteBatchRequests { get; private set; }
        public int LastUpsertDocumentCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.Trim('/');
            if (request.Method == HttpMethod.Get && path.StartsWith("indexes/", StringComparison.Ordinal))
                return indexCreated ? Json(HttpStatusCode.OK, new { uid = "index", primaryKey = "id" }) : Json(HttpStatusCode.NotFound, new { message = "missing" });
            if (request.Method == HttpMethod.Post && path == "indexes")
            {
                IndexCreateRequests++;
                return Enqueued(++taskId);
            }
            if (request.Method == HttpMethod.Patch && path.EndsWith("/settings", StringComparison.Ordinal))
            {
                SettingsRequests++;
                return Enqueued(++taskId);
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/documents", StringComparison.Ordinal))
            {
                UpsertRequests++;
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                LastUpsertDocumentCount = json.RootElement.GetArrayLength();
                return Enqueued(++taskId);
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/documents/delete-batch", StringComparison.Ordinal))
            {
                DeleteBatchRequests++;
                return Enqueued(++taskId);
            }
            if (request.Method == HttpMethod.Delete && path.Contains("/documents/", StringComparison.Ordinal))
                return Enqueued(++taskId);
            if (request.Method == HttpMethod.Get && path.StartsWith("tasks/", StringComparison.Ordinal))
            {
                if (Interlocked.Exchange(ref failPoll, 0) == 1) return Json(HttpStatusCode.ServiceUnavailable, new { message = "temporary" });
                var id = long.Parse(path["tasks/".Length..], System.Globalization.CultureInfo.InvariantCulture);
                if (id == 1) indexCreated = true;
                return Json(HttpStatusCode.OK, new { uid = id, status = "succeeded", type = id == 1 ? "indexCreation" : "documentAdditionOrUpdate" });
            }
            return Json(HttpStatusCode.NotFound, new { message = "unhandled" });
        }

        static HttpResponseMessage Enqueued(long id) => Json(HttpStatusCode.Accepted, new { taskUid = id });
        static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status) { Content = JsonContent.Create(value) };
    }
}
