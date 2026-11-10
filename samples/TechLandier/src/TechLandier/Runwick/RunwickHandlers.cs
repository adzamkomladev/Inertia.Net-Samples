using Runwick;

namespace TechLandier.Infrastructure;

/// <summary>The five demo jobs share one handler; only the simulated work differs.</summary>
public sealed class DemoSleepHandler(ILogger<DemoSleepHandler> logger, IJobContext context)
    : global::Runwick.IJobHandler<DemoSleep100ms>,
      global::Runwick.IJobHandler<DemoSleep500ms>,
      global::Runwick.IJobHandler<DemoSleep2s>,
      global::Runwick.IJobHandler<DemoSleep10s>,
      global::Runwick.IJobHandler<DemoSleep30s>
{
    public async Task HandleAsync(DemoSleep100ms job, CancellationToken ct) =>
        await RunAsync("100ms", job.BatchId, job.Index, TimeSpan.FromMilliseconds(100), ct);

    public async Task HandleAsync(DemoSleep500ms job, CancellationToken ct) =>
        await RunAsync("500ms", job.BatchId, job.Index, TimeSpan.FromMilliseconds(500), ct);

    public async Task HandleAsync(DemoSleep2s job, CancellationToken ct) =>
        await RunAsync("2s", job.BatchId, job.Index, TimeSpan.FromSeconds(2), ct);

    public async Task HandleAsync(DemoSleep10s job, CancellationToken ct) =>
        await RunAsync("10s", job.BatchId, job.Index, TimeSpan.FromSeconds(10), ct);

    public async Task HandleAsync(DemoSleep30s job, CancellationToken ct) =>
        await RunAsync("30s", job.BatchId, job.Index, TimeSpan.FromSeconds(30), ct);

    private async Task RunAsync(string kind, Guid batchId, int index, TimeSpan delay, CancellationToken ct)
    {
        logger.LogInformation(
            "Runwick demo {Kind} started: job {JobId}, batch {BatchId}, item {Index}, execution {Execution}",
            kind, context.JobId, batchId, index, context.ExecutionNumber);
        await Task.Delay(delay, ct);
        logger.LogInformation(
            "Runwick demo {Kind} finished: job {JobId}, batch {BatchId}, item {Index}, execution {Execution}",
            kind, context.JobId, batchId, index, context.ExecutionNumber);
    }
}

/// <summary>Runs one <see cref="CronJobs.Schedule"/> entry inside Runwick's worker scope.</summary>
public sealed class ScheduledCronJobHandler(CronTasks tasks) : global::Runwick.IJobHandler<ScheduledCronJob>
{
    public async Task HandleAsync(ScheduledCronJob job, CancellationToken ct)
    {
        if (!CronJobs.TryGet(job.Name, out var entry))
            throw new global::Runwick.PermanentJobFailureException("cron_unknown", $"No cron task named '{job.Name}'.");
        await entry.Run(tasks, ct);
    }
}
