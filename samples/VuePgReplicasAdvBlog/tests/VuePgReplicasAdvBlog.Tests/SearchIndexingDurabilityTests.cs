using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Search;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Tests;

public sealed class SearchIndexingDurabilityTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Setup_and_post_tasks_resume_after_restart_and_keep_the_indexed_version_fenced()
    {
        var postId = await factory.QueryAsync(async db =>
        {
            var author = new User { Email = $"search-{Guid.NewGuid():N}@example.com", Name = "Search Author" };
            db.Users.Add(author);
            await db.SaveChangesAsync();
            var post = new Post
            {
                AuthorId = author.Id,
                Title = "Initial title",
                Slug = $"initial-{Guid.NewGuid():N}",
                Excerpt = "Initial excerpt",
                Body = "Initial body",
                IndexVersion = 7,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PublishedAt = DateTime.UtcNow,
            };
            db.Posts.Add(post);
            await db.SaveChangesAsync();
            db.SearchIndexStates.Add(new SearchIndexState { PostId = post.Id, RequestedVersion = 7 });
            await db.SaveChangesAsync();
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
        var serviceNamespace = new OptionalServiceNamespace("VuePgReplicasAdvBlog", "Development");

        await using (var firstScope = factory.Services.CreateAsyncScope())
        {
            var db = firstScope.ServiceProvider.GetRequiredService<AppDb>();
            await Assert.ThrowsAsync<HttpRequestException>(() => SearchIndexing.ProcessAsync(db, client, serviceNamespace, postId));
        }
        var createAfterRestart = await factory.QueryAsync(db => Task.FromResult(db.SearchIndexStates.AsNoTracking().Single(state => state.PostId == Guid.Empty)));
        Assert.Equal(1L, createAfterRestart.TaskVersion);
        Assert.Equal(1L, createAfterRestart.TaskId);
        Assert.Equal(0L, createAfterRestart.AppliedVersion);

        await using (var retryScope = factory.Services.CreateAsyncScope())
        {
            var db = retryScope.ServiceProvider.GetRequiredService<AppDb>();
            await Assert.ThrowsAsync<HttpRequestException>(() => SearchIndexing.ProcessAsync(db, client, serviceNamespace, postId));
        }
        var settingsAfterRestart = await factory.QueryAsync(db => Task.FromResult(db.SearchIndexStates.AsNoTracking().Single(state => state.PostId == Guid.Empty)));
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
        var final = await factory.QueryAsync(async db => new
        {
            Setup = await db.SearchIndexStates.AsNoTracking().SingleAsync(state => state.PostId == Guid.Empty),
            Post = await db.SearchIndexStates.AsNoTracking().SingleAsync(state => state.PostId == postId),
        });
        Assert.Equal(2L, final.Setup.AppliedVersion);
        Assert.Null(final.Setup.TaskId);
        Assert.Equal(8L, final.Post.RequestedVersion);
        Assert.Equal(8L, final.Post.AppliedVersion);
        Assert.Null(final.Post.TaskId);
        Assert.Null(final.Post.TaskVersion);
    }

    static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
