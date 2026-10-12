using SvelteMongoBlog.Data;
using MongoDB.Driver;

namespace SvelteMongoBlog.Infrastructure;

// Each consumer atomically claims one due document; no buffered leases or relational SQL.
public sealed class JobWorker(IServiceScopeFactory scopes, JobQueue queue, JobRegistry registry, TimeProvider time,
    IConfiguration config, ILogger<JobWorker> logger) : BackgroundService
{
    internal const int MaxAttempts = 5;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private DateTime Now => new(time.GetUtcNow().UtcTicks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

    protected override Task ExecuteAsync(CancellationToken ct) => Task.WhenAll(
        Enumerable.Range(0, Math.Max(1, config.GetValue("Jobs:Concurrency", 4))).Select(_ => ConsumeAsync(ct)));

    private async Task ConsumeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var job = await ClaimAsync(ct);
                if (job is null) { await queue.WaitForWakeAsync(TimeSpan.FromSeconds(10), ct); continue; }
                await ProcessAsync(job, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job worker failed; retrying");
                try { await Task.Delay(TimeSpan.FromSeconds(5), time, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            }
        }
    }

    internal async Task<Job?> ClaimAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var now = Now;
        return await db.Jobs.FindOneAndUpdateAsync(j =>
            (j.Status == JobStatus.Pending && j.RunAt <= now) ||
            (j.Status == JobStatus.Running && (j.LockedUntil == null || j.LockedUntil < now)),
            Builders<Job>.Update.Set(j => j.Status, JobStatus.Running).Set(j => j.LockedUntil, now + Lease),
            new FindOneAndUpdateOptions<Job> { Sort = Builders<Job>.Sort.Ascending(j => j.RunAt).Ascending(j => j.Id), ReturnDocument = ReturnDocument.After }, ct);
    }

    internal async Task ProcessAsync(Job job, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var lease = Now + Lease;
        if (lease == job.LockedUntil) lease = lease.AddMilliseconds(1);
        var taken = await db.Jobs.FindOneAndUpdateAsync(j => j.Id == job.Id && j.Status == JobStatus.Running && j.LockedUntil == job.LockedUntil,
            Builders<Job>.Update.Set(j => j.LockedUntil, lease),
            new FindOneAndUpdateOptions<Job> { ReturnDocument = ReturnDocument.After }, ct);
        if (taken is null) return;
        job = taken;
        var status = JobStatus.Done;
        var runAt = job.RunAt;
        string? error = null;
        var attempts = job.Attempts;
        try
        {
            if (!registry.TryGet(job.Type, out var run)) throw new InvalidOperationException($"No handler for {job.Type}");
            // ponytail: handlers exceeding five minutes can be reclaimed; renew leases if long tasks are added.
            await run(scope.ServiceProvider, job.Payload, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { status = JobStatus.Pending; }
        catch (Exception ex)
        {
            attempts++;
            status = attempts >= MaxAttempts ? JobStatus.Failed : JobStatus.Pending;
            runAt = Now.AddMinutes(Math.Pow(2, attempts - 1));
            error = ex.ToString();
            if (error.Length > 2000) error = error[..2000];
            logger.LogWarning(ex, "Job {JobId} failed on attempt {Attempts}", job.Id, attempts);
        }
        await db.Jobs.UpdateOneAsync(j => j.Id == job.Id && j.Status == JobStatus.Running && j.LockedUntil == job.LockedUntil,
            Builders<Job>.Update.Set(j => j.Status, status).Set(j => j.Attempts, attempts).Set(j => j.RunAt, runAt)
                .Set(j => j.LastError, error).Set(j => j.LockedUntil, null), cancellationToken: CancellationToken.None);
    }
}
