using System.Net;
using System.Text;
using System.Text.Json;
using ReactSqliteBlog.Data;
using ReactSqliteBlog.Infrastructure;
using ReactSqliteBlog.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ReactSqliteBlog.Tests;

public sealed class SearchIndexingDurabilityTests
{
    [Fact]
    public async Task Full_reindex_uses_bulk_tasks_and_persists_the_next_500_post_cursor()
    {
        using var factory = new BlogFactory();
        var authorId = await factory.CreateUserAsync(BlogFactory.NewEmail("reindex"));
        var postIds = factory.WithDb(db =>
        {
            var now = DateTime.UtcNow;
            // UUIDv7 is only ordered across milliseconds; sort so post i has the i-th smallest id.
            var ids = Enumerable.Range(0, 501).Select(_ => Guid.CreateVersion7()).Order().ToArray();
            db.Posts.AddRange(Enumerable.Range(0, 501).Select(i => new Post
            {
                Id = ids[i], AuthorId = authorId, Title = $"reindex-{i:D3}", Body = "batch document",
                CreatedAt = now, UpdatedAt = now, PublishedAt = now, ArchivedAt = i == 0 ? now : null,
                IndexVersion = 1,
            }));
            db.SaveChanges();
            // Match the worker's explicit empty-key sentinel; EF treats Guid.Empty as an unset generated key.
            db.Database.ExecuteSql($"""INSERT INTO "SearchIndexStates" ("PostId", "RequestedVersion", "AppliedVersion") VALUES ({Guid.Empty}, 0, 2)""");
            return db.Posts.IgnoreQueryFilters().Where(post => post.Title.StartsWith("reindex-")).OrderBy(post => post.Id)
                .Select(post => post.Id).ToArray();
        });

        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/documents", StringComparison.Ordinal))
                return Task.FromResult(Json(HttpStatusCode.Accepted, """{"taskUid":31}"""));
            if (request.Method == HttpMethod.Post && path.EndsWith("/documents/delete-batch", StringComparison.Ordinal))
                return Task.FromResult(Json(HttpStatusCode.Accepted, """{"taskUid":32}"""));
            if (request.Method == HttpMethod.Get && path == "/tasks/31")
                return Task.FromResult(Json(HttpStatusCode.OK, """{"uid":31,"status":"succeeded"}"""));
            if (request.Method == HttpMethod.Get && path == "/tasks/32")
                return Task.FromResult(Json(HttpStatusCode.OK, """{"uid":32,"status":"succeeded"}"""));
            throw new InvalidOperationException($"Unexpected Meilisearch request {request.Method} {path}");
        });
        using var http = new HttpClient(handler);
        var client = new MeilisearchTaskClient(http, new MeilisearchConnectionSettings(new Uri("https://search.example/"), null));
        var ns = new OptionalServiceNamespace("ReactSqliteBlog", "Testing");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var jobs = scope.ServiceProvider.GetRequiredService<JobQueue>();

        Assert.True(await SearchIndexing.RequestFullReindexAsync(db, jobs));
        Assert.False(await SearchIndexing.RequestFullReindexAsync(db, jobs));
        var initial = await db.Jobs.SingleAsync(job => job.Type == nameof(ReindexSearchBatch));
        initial.Status = JobStatus.Running;
        await db.SaveChangesAsync();
        await SearchIndexing.ProcessReindexBatchAsync(db, jobs, client, ns, Guid.Empty);

        Assert.Equal(1, handler.Paths.Count(path => path.EndsWith("/documents", StringComparison.Ordinal)));
        Assert.Equal(1, handler.Paths.Count(path => path.EndsWith("/documents/delete-batch", StringComparison.Ordinal)));
        var bulkBody = handler.Bodies[handler.Paths.FindIndex(path => path.EndsWith("/documents", StringComparison.Ordinal))];
        Assert.Contains("reindex-001", bulkBody, StringComparison.Ordinal);
        Assert.DoesNotContain("reindex-000", bulkBody, StringComparison.Ordinal);
        var deleteBody = handler.Bodies[handler.Paths.FindIndex(path => path.EndsWith("/documents/delete-batch", StringComparison.Ordinal))];
        Assert.Contains(postIds[0].ToString(), deleteBody);
        var continuation = await db.Jobs.SingleAsync(job => job.Type == nameof(ReindexSearchBatch) && job.Status == JobStatus.Pending);
        var payload = JsonSerializer.Deserialize<ReindexSearchBatch>(continuation.Payload, JobQueue.Json)!;
        Assert.Equal(postIds[499], payload.AfterPostId);
        Assert.Equal(501, await db.SearchIndexStates.CountAsync()); // 500 posts in the first batch plus the setup sentinel
    }

    [Fact]
    public async Task Setup_and_post_tasks_resume_after_restart_and_keep_the_indexed_version_fenced()
    {
        using var factory = new BlogFactory();
        var authorId = await factory.CreateUserAsync(BlogFactory.NewEmail("search"));
        var postId = factory.WithDb(db =>
        {
            var post = new Post
            {
                AuthorId = authorId, Title = "Initial title", Body = "Initial body",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, PublishedAt = DateTime.UtcNow,
                IndexVersion = 7,
            };
            db.Posts.Add(post);
            db.SaveChanges();
            db.SearchIndexStates.Add(new SearchIndexState { PostId = post.Id, RequestedVersion = 7 });
            db.SaveChanges();
            return post.Id;
        });

        var firstCreatePoll = 0;
        var firstSettingsPoll = 0;
        var createTaskCount = 0;
        var settingsTaskCount = 0;
        var documentTaskCount = 0;
        long? capturedVersion = null;
        var handler = new RecordingHandler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.StartsWith("/indexes/", StringComparison.Ordinal))
                return new(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Post && path == "/indexes")
            {
                Interlocked.Increment(ref createTaskCount);
                return Json(HttpStatusCode.Accepted, """{"taskUid":1,"status":"enqueued"}""");
            }
            if (request.Method == HttpMethod.Get && path == "/tasks/1")
            {
                if (Interlocked.Exchange(ref firstCreatePoll, 1) == 0) throw new HttpRequestException("simulated process interruption");
                return Json(HttpStatusCode.OK, """{"uid":1,"status":"succeeded"}""");
            }
            if (request.Method == HttpMethod.Patch && path.EndsWith("/settings", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref settingsTaskCount);
                return Json(HttpStatusCode.Accepted, """{"taskUid":2,"status":"enqueued"}""");
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/documents", StringComparison.Ordinal))
                return Json(HttpStatusCode.Accepted, $"{{\"taskUid\":{2 + Interlocked.Increment(ref documentTaskCount)},\"status\":\"enqueued\"}}");
            if (request.Method == HttpMethod.Get && path == "/tasks/2")
            {
                if (Interlocked.Exchange(ref firstSettingsPoll, 1) == 0) throw new HttpRequestException("simulated process interruption");
                return Json(HttpStatusCode.OK, """{"uid":2,"status":"succeeded"}""");
            }
            if (request.Method == HttpMethod.Get && path is "/tasks/3" or "/tasks/4")
            {
                if (path == "/tasks/3")
                {
                    await using var mutationScope = factory.Services.CreateAsyncScope();
                    var mutationDb = mutationScope.ServiceProvider.GetRequiredService<AppDb>();
                    capturedVersion = await mutationDb.SearchIndexStates.Where(state => state.PostId == postId)
                        .Select(state => state.TaskVersion).SingleAsync();
                    await mutationDb.Posts.Where(post => post.Id == postId).ExecuteUpdateAsync(setters => setters
                        .SetProperty(post => post.Title, "Updated during indexing")
                        .SetProperty(post => post.IndexVersion, 8));
                    await mutationDb.SearchIndexStates.Where(state => state.PostId == postId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(state => state.RequestedVersion, 8));
                }
                var taskId = path == "/tasks/3" ? 3 : 4;
                return Json(HttpStatusCode.OK, $"{{\"uid\":{taskId},\"status\":\"succeeded\"}}");
            }
            throw new InvalidOperationException($"Unexpected Meilisearch request {request.Method} {path}");
        });
        using var http = new HttpClient(handler);
        var client = new MeilisearchTaskClient(http, new MeilisearchConnectionSettings(new Uri("https://search.example/"), "test"));
        var serviceNamespace = new OptionalServiceNamespace("ReactSqliteBlog", "Testing");

        await using (var firstScope = factory.Services.CreateAsyncScope())
        {
            var db = firstScope.ServiceProvider.GetRequiredService<AppDb>();
            await Assert.ThrowsAsync<HttpRequestException>(() => SearchIndexing.ProcessAsync(db, client, serviceNamespace, postId));
        }
        var setupAfterRestart = factory.WithDb(db => db.SearchIndexStates.AsNoTracking().Single(state => state.PostId == Guid.Empty));
        Assert.Equal(1, setupAfterRestart.TaskVersion);
        Assert.Equal(1, setupAfterRestart.TaskId);
        Assert.Equal(0, setupAfterRestart.AppliedVersion);

        await using (var retryScope = factory.Services.CreateAsyncScope())
        {
            var db = retryScope.ServiceProvider.GetRequiredService<AppDb>();
            await Assert.ThrowsAsync<HttpRequestException>(() => SearchIndexing.ProcessAsync(db, client, serviceNamespace, postId));
        }
        var settingsAfterRestart = factory.WithDb(db => db.SearchIndexStates.AsNoTracking().Single(state => state.PostId == Guid.Empty));
        Assert.Equal(1L, settingsAfterRestart.AppliedVersion);
        Assert.Equal(2L, settingsAfterRestart.TaskVersion);
        Assert.Equal(2L, settingsAfterRestart.TaskId);

        await using (var finalScope = factory.Services.CreateAsyncScope())
        {
            var db = finalScope.ServiceProvider.GetRequiredService<AppDb>();
            await SearchIndexing.ProcessAsync(db, client, serviceNamespace, postId);
        }

        Assert.Equal(7L, capturedVersion);
        Assert.Equal(1, createTaskCount);
        Assert.Equal(1, settingsTaskCount);
        Assert.Equal(2, documentTaskCount);
        var final = factory.WithDb(db => new
        {
            Setup = db.SearchIndexStates.AsNoTracking().Single(state => state.PostId == Guid.Empty),
            Post = db.SearchIndexStates.AsNoTracking().Single(state => state.PostId == postId),
        });
        Assert.Equal(2, final.Setup.AppliedVersion);
        Assert.Null(final.Setup.TaskId);
        Assert.Equal(8, final.Post.RequestedVersion);
        Assert.Equal(8, final.Post.AppliedVersion);
        Assert.Null(final.Post.TaskId);
        Assert.Null(final.Post.TaskVersion);
    }

    static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return await send(request);
        }
    }
}
