using ReactSqliteReplicasBlog.Data;
using ReactSqliteReplicasBlog.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ReactSqliteReplicasBlog.Tests;

public sealed record Boom(int N);
public sealed record Ok(int N);

public sealed class BoomHandler : IJobHandler<Boom>
{
    public Task HandleAsync(Boom payload, CancellationToken ct) => throw new InvalidOperationException("boom");
}

public sealed class OkHandler : IJobHandler<Ok>
{
    public Task HandleAsync(Ok payload, CancellationToken ct) => Task.CompletedTask;
}

public class JobWorkerTests
{
    private static async Task<(TestServices, JobWorker)> SetupAsync()
    {
        var s = await TestServices.CreateAsync(sc => sc.AddJobHandler<Boom, BoomHandler>().AddJobHandler<Ok, OkHandler>());
        var p = s.Provider;
        var worker = new JobWorker(p.GetRequiredService<IServiceScopeFactory>(), p.GetRequiredService<JobQueue>(),
            p.GetRequiredService<JobRegistry>(), s.Time, new ConfigurationBuilder().Build(), NullLogger<JobWorker>.Instance);
        return (s, worker);
    }

    private static async Task<Guid> EnqueueAsync<T>(TestServices s, T payload) where T : notnull
    {
        await using var db = s.NewDb();
        var job = s.Provider.GetRequiredService<JobQueue>().Enqueue(db, payload);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private static async Task<Job> LoadAsync(TestServices s, Guid id)
    {
        await using var db = s.NewDb();
        return await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == id);
    }

    [Fact]
    public async Task Succeeding_handler_marks_job_done()
    {
        var (s, worker) = await SetupAsync();
        await using var _ = s;
        var id = await EnqueueAsync(s, new Ok(1));

        Assert.Equal([id], await worker.ClaimBatchAsync(default));
        Assert.Equal(JobStatus.Running, (await LoadAsync(s, id)).Status);
        await worker.ProcessAsync(id, default);

        var job = await LoadAsync(s, id);
        Assert.Equal(JobStatus.Done, job.Status);
        Assert.Null(job.LockedUntil);
        Assert.Equal(0, job.Attempts);
    }

    [Fact]
    public async Task Failing_handler_backs_off_then_fails_after_five_attempts()
    {
        var (s, worker) = await SetupAsync();
        await using var _ = s;
        var id = await EnqueueAsync(s, new Boom(1));

        foreach (var minutes in new[] { 1, 2, 4, 8 })
        {
            Assert.Equal([id], await worker.ClaimBatchAsync(default));
            await worker.ProcessAsync(id, default);

            var job = await LoadAsync(s, id);
            Assert.Equal(JobStatus.Pending, job.Status);
            Assert.Equal(s.Now.AddMinutes(minutes), job.RunAt);
            Assert.Contains("boom", job.LastError);

            Assert.Empty(await worker.ClaimBatchAsync(default)); // not due yet
            s.Time.Advance(TimeSpan.FromMinutes(minutes));
        }

        Assert.Equal([id], await worker.ClaimBatchAsync(default));
        await worker.ProcessAsync(id, default);
        var failed = await LoadAsync(s, id);
        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Equal(5, failed.Attempts);
        Assert.Null(failed.LockedUntil);
    }

    [Fact]
    public async Task Running_worker_processes_woken_jobs_and_stops_cleanly()
    {
        var (s, worker) = await SetupAsync();
        await using var _ = s;
        await worker.StartAsync(default);
        var id = await EnqueueAsync(s, new Ok(1));
        s.Provider.GetRequiredService<JobQueue>().Wake();

        for (var i = 0; i < 50 && (await LoadAsync(s, id)).Status != JobStatus.Done; i++) await Task.Delay(100);
        Assert.Equal(JobStatus.Done, (await LoadAsync(s, id)).Status);

        await worker.StopAsync(default);
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Unknown_type_fails()
    {
        var (s, worker) = await SetupAsync();
        await using var _ = s;
        var id = await EnqueueAsync(s, new WeeklyDigestEmail(Guid.CreateVersion7())); // no handler registered

        await worker.ClaimBatchAsync(default);
        await worker.ProcessAsync(id, default);

        var job = await LoadAsync(s, id);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("No handler", job.LastError);
    }

    [Fact]
    public async Task Concurrent_claims_never_return_the_same_id()
    {
        var (s, worker) = await SetupAsync();
        await using var _ = s;
        await using (var db = s.NewDb())
        {
            var queue = s.Provider.GetRequiredService<JobQueue>();
            for (var i = 0; i < 300; i++) queue.Enqueue(db, new Ok(i));
            await db.SaveChangesAsync();
        }

        var batches = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => worker.ClaimBatchAsync(default))));
        var all = batches.SelectMany(b => b).ToList();

        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(batches, b => Assert.True(b.Count <= JobWorker.BatchSize));
        Assert.Equal(300, all.Count); // 8 x 50 >= 300: everything claimed exactly once
    }

    [Fact]
    public async Task Startup_reset_returns_stale_running_jobs_to_pending()
    {
        var (s, worker) = await SetupAsync();
        await using var _ = s;
        var stale = await EnqueueAsync(s, new Ok(1));
        var live = await EnqueueAsync(s, new Ok(2));
        await worker.ClaimBatchAsync(default); // both Running, locked for 5 minutes

        await using (var db = s.NewDb())
            await db.Jobs.Where(j => j.Id == stale).ExecuteUpdateAsync(x => x.SetProperty(j => j.LockedUntil, s.Now.AddSeconds(-1)));

        await worker.ResetExpiredLocksAsync(default);

        Assert.Equal(JobStatus.Pending, (await LoadAsync(s, stale)).Status);
        Assert.Null((await LoadAsync(s, stale)).LockedUntil);
        Assert.Equal(JobStatus.Running, (await LoadAsync(s, live)).Status);
    }
}
