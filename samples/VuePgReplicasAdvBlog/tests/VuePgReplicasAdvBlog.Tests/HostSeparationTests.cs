extern alias main;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VuePgReplicasAdvBlog.Features.Analytics;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Tests;

public sealed class HostSeparationTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Hosts_expose_only_their_own_routes_and_background_work()
    {
        using var main = app.Client();
        using var absentHub = await main.GetAsync("/realtime");
        Assert.Equal(HttpStatusCode.NotFound, absentHub.StatusCode);
        Assert.Null(app.Services.GetService<JobRegistry>());
        Assert.DoesNotContain(app.Services.GetServices<IHostedService>(), service => service is JobWorker);
        Assert.Contains(app.WorkerServices.GetServices<IHostedService>(), service => service is JobWorker);
        using var realtime = app.RealtimeClient();
        using var absentPage = await realtime.GetAsync("/posts");
        Assert.Equal(HttpStatusCode.NotFound, absentPage.StatusCode);
        Assert.DoesNotContain(typeof(main::Program).Assembly.GetReferencedAssemblies(), name => name.Name is "Worker" or "Realtime");
        Assert.DoesNotContain(typeof(Worker.WorkerSetup).Assembly.GetReferencedAssemblies(), name => name.Name is "MainApp" or "Realtime");
        Assert.DoesNotContain(typeof(Realtime.RealtimeHost).Assembly.GetReferencedAssemblies(), name => name.Name is "MainApp" or "Worker");
    }

    [Fact]
    public async Task A_beacon_survives_worker_downtime_and_retry_does_not_double_count()
    {
        var title = "Durable beacon " + Guid.NewGuid().ToString("N");
        var (author, _) = await app.SignUpAsync($"durable-view-{Guid.NewGuid():N}@example.test");
        using (await app.SendAsync(author, "/posts", new { title, body = "Body" })) { }
        var postId = await app.PostIdAsync(title);
        using var reader = app.Client();
        reader.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 TestBrowser");
        await app.StopWorkerAsync();
        using var beacon = await app.SendAsync(reader, $"/posts/{postId}/view", new { referrer = "https://durable.example/story" });
        Assert.Equal(HttpStatusCode.NoContent, beacon.StatusCode);
        var payload = await app.QueryAsync(db => db.Jobs.Where(job => job.Type == nameof(RecordView) && job.Payload.Contains(postId.ToString()))
            .Select(job => job.Payload).SingleAsync());
        Assert.Equal(0, await app.QueryAsync(db => db.PostViews.CountAsync(view => view.PostId == postId)));
        _ = app.WorkerServices;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && await app.QueryAsync(db => db.PostViews.CountAsync(view => view.PostId == postId)) == 0)
            await Task.Delay(100);
        Assert.Equal(1, await app.QueryAsync(db => db.PostViews.CountAsync(view => view.PostId == postId)));
        await using var scope = app.WorkerServices.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<RecordViewHandler>();
        var view = System.Text.Json.JsonSerializer.Deserialize<RecordView>(payload)!;
        await handler.HandleAsync(view, default);
        Assert.Equal(1, await app.QueryAsync(db => db.PostViews.CountAsync(row => row.PostId == postId)));
        await app.QueryAsync(async db => { await db.Posts.Where(post => post.Id == postId).ExecuteDeleteAsync(); return true; });
        await handler.HandleAsync(view with { Id = Guid.CreateVersion7() }, default);
        Assert.Equal(0, await app.QueryAsync(db => db.PostViews.CountAsync(row => row.PostId == postId)));
    }
}
