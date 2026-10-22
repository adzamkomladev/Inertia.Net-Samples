using System.Threading.Channels;
using Inkwell.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Jobs;

/// <summary>
/// Claims due jobs from SQLite and fans them out over a bounded Channel to concurrent consumers.
/// A single dispatcher claims rows atomically (UPDATE ... RETURNING); N consumers run the handlers.
/// The claim's LockedUntil value doubles as an ownership token: a consumer only starts a job whose lock still matches
/// the claim, and only records the outcome while it still holds its own lease. A job whose lease expired while it was
/// buffered (and was reclaimed) is therefore never run or completed by a stale copy.
/// </summary>
public sealed class JobWorker(
    IServiceScopeFactory scopes,
    JobQueue queue,
    JobRegistry registry,
    TimeProvider time,
    IConfiguration config,
    ILogger<JobWorker> logger) : BackgroundService
{
    internal const int BatchSize = 50;
    internal const int MaxAttempts = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);

    private readonly record struct Claim(Guid Id, DateTime Lock);

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Jobs:Enabled", true)) return;
        await ResetExpiredLocksAsync(stoppingToken); // crash or redeploy mid-job

        var concurrency = Math.Max(1, config.GetValue("Jobs:Concurrency", 4));
        // Small buffer: claimed jobs shouldn't wait long enough for their lease to expire.
        var channel = Channel.CreateBounded<Claim>(new BoundedChannelOptions(concurrency * 2)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
        });
        var consumers = Enumerable.Range(0, concurrency).Select(async _ => await ConsumeAsync(channel.Reader, stoppingToken)).ToArray();
        var pending = new Queue<Claim>(); // claimed but not yet handed to the channel

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                int claimed;
                try
                {
                    var (ids, lockToken) = await ClaimAsync(stoppingToken);
                    foreach (var id in ids) pending.Enqueue(new Claim(id, lockToken));
                    claimed = ids.Count;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // e.g. SQLITE_BUSY past the timeout: a transient DB error must never stop the host.
                    logger.LogError(ex, "Job dispatcher failed to claim jobs; retrying in {Delay}", ErrorBackoff);
                    await Task.Delay(ErrorBackoff, time, stoppingToken);
                    continue;
                }

                while (pending.TryPeek(out var next))
                {
                    await channel.Writer.WriteAsync(next, stoppingToken);
                    pending.Dequeue();
                }
                if (claimed < BatchSize) await queue.WaitForWakeAsync(PollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            channel.Writer.TryComplete();
            await Task.WhenAll(consumers);
            // Claimed but never started: hand them back so the next process picks them up right away.
            while (channel.Reader.TryRead(out var c)) pending.Enqueue(c);
            foreach (var c in pending) await ReleaseAsync(c.Id, c.Lock);
        }
    }

    private async Task ConsumeAsync(ChannelReader<Claim> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var claim in reader.ReadAllAsync(ct))
            {
                try { await ProcessAsync(claim.Id, claim.Lock, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                catch (Exception ex) { logger.LogError(ex, "Job {JobId}: worker error", claim.Id); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    /// <summary>Atomically marks up to <see cref="BatchSize"/> due Pending jobs as Running and returns their ids.</summary>
    internal async Task<List<Guid>> ClaimBatchAsync(CancellationToken ct) => (await ClaimAsync(ct)).Ids;

    /// <summary>Claims a batch. Every claimed row gets the same LockedUntil, returned as the ownership token.</summary>
    private async Task<(List<Guid> Ids, DateTime Lock)> ClaimAsync(CancellationToken ct)
    {
        await ResetExpiredLocksAsync(ct); // a hung handler

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var conn = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Jobs SET Status = 1, LockedUntil = $lock, UpdatedAt = $now
            WHERE Id IN (SELECT Id FROM Jobs WHERE Status = 0 AND RunAt <= $now ORDER BY RunAt LIMIT $limit)
            RETURNING Id
            """;
        var now = Now;
        var lockToken = now + LockDuration;
        AddParam(cmd, "$now", now);
        AddParam(cmd, "$lock", lockToken);
        AddParam(cmd, "$limit", BatchSize);

        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        return (ids, lockToken);
    }

    /// <summary>Puts Running jobs whose lock expired (or was never set) back to Pending.</summary>
    internal async Task ResetExpiredLocksAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var now = Now;
        var count = await db.Jobs
            .Where(j => j.Status == JobStatus.Running && (j.LockedUntil == null || j.LockedUntil < now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Pending)
                .SetProperty(j => j.LockedUntil, (DateTime?)null)
                .SetProperty(j => j.UpdatedAt, now), ct);
        if (count > 0) logger.LogWarning("Reset {Count} job(s) with expired locks to Pending", count);
    }

    /// <summary>Runs one claimed job, using its current lock as the claim token (lets tests skip the channel).</summary>
    internal async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        DateTime? lockToken;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            lockToken = await db.Jobs.Where(j => j.Id == id).Select(j => j.LockedUntil).FirstOrDefaultAsync(ct);
        }
        if (lockToken is { } t) await ProcessAsync(id, t, ct);
    }

    /// <summary>Runs one claimed job and records the outcome. Never throws for handler failures.</summary>
    private async Task ProcessAsync(Guid id, DateTime claimLock, CancellationToken ct)
    {
        Job? job;
        Exception? error = null;
        DateTime lease;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();

            // Take a fresh lease, but only if the dispatcher's claim is still current. If the lease expired while the
            // job sat in the buffer, it was reset (and maybe reclaimed and run elsewhere): skip this stale copy.
            lease = Now + LockDuration;
            if (lease == claimLock) lease = lease.AddTicks(10);
            var taken = await db.Jobs
                .Where(j => j.Id == id && j.Status == JobStatus.Running && j.LockedUntil == claimLock)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.LockedUntil, lease), ct);
            if (taken == 0) return;

            job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
            if (job is null) return;

            if (!registry.TryGet(job.Type, out var run))
            {
                logger.LogError("Job {JobId}: no handler for type {Type}", id, job.Type);
                await SaveAsync(id, lease, JobStatus.Failed, job.Attempts, job.RunAt, $"No handler registered for job type '{job.Type}'.");
                return;
            }

            // ponytail: a handler running longer than LockDuration can be reclaimed and run twice; renew the lease
            // periodically if long-running jobs are ever added. E-mails finish in seconds.
            try { await run(scope.ServiceProvider, job.Payload, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await ReleaseAsync(id, lease); // shutdown: retry later without counting an attempt
                return;
            }
            catch (Exception ex) { error = ex; }
        }

        // The handler's scope (and any half-done tracked changes) is gone; record the result on a fresh context.
        if (error is null)
        {
            await SaveAsync(id, lease, JobStatus.Done, job.Attempts, job.RunAt, job.LastError);
            return;
        }

        var attempts = job.Attempts + 1;
        var message = error.ToString();
        if (message.Length > 2000) message = message[..2000];
        if (attempts >= MaxAttempts)
        {
            logger.LogError(error, "Job {JobId} ({Type}) failed permanently after {Attempts} attempts", id, job.Type, attempts);
            await SaveAsync(id, lease, JobStatus.Failed, attempts, job.RunAt, message);
        }
        else
        {
            var delay = TimeSpan.FromMinutes(Math.Pow(2, attempts - 1)); // 1, 2, 4, 8 min
            logger.LogWarning(error, "Job {JobId} ({Type}) failed, attempt {Attempts}; retrying in {Delay}", id, job.Type, attempts, delay);
            await SaveAsync(id, lease, JobStatus.Pending, attempts, Now + delay, message);
        }
    }

    // Outcomes are written with CancellationToken.None so a shutdown can't lose them, and only while this consumer
    // still holds its lease, so a stale worker never overwrites a newer outcome.
    private async Task SaveAsync(Guid id, DateTime lease, JobStatus status, int attempts, DateTime runAt, string? lastError)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var now = Now;
        var saved = await db.Jobs
            .Where(j => j.Id == id && j.Status == JobStatus.Running && j.LockedUntil == lease)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, status)
                .SetProperty(j => j.Attempts, attempts)
                .SetProperty(j => j.RunAt, runAt)
                .SetProperty(j => j.LastError, lastError)
                .SetProperty(j => j.LockedUntil, (DateTime?)null)
                .SetProperty(j => j.UpdatedAt, now), CancellationToken.None);
        if (saved == 0) logger.LogWarning("Job {JobId}: lease lost before its outcome ({Status}) was recorded", id, status);
    }

    private async Task ReleaseAsync(Guid id, DateTime lockToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var now = Now;
        await db.Jobs
            .Where(j => j.Id == id && j.Status == JobStatus.Running && j.LockedUntil == lockToken)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Pending)
                .SetProperty(j => j.LockedUntil, (DateTime?)null)
                .SetProperty(j => j.UpdatedAt, now), CancellationToken.None);
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
