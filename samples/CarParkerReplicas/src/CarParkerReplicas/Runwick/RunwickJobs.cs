using Runwick;

namespace CarParker.Infrastructure;

// Demo payloads. Each type maps to one stable Runwick job name and one queue/pool configuration,
// so the dashboard shows fixed and autoscaling workers doing different amounts of work.
public sealed record DemoSleep100ms(Guid BatchId, int Index, DateTimeOffset EnqueuedAt) : IQueueJob;
public sealed record DemoSleep500ms(Guid BatchId, int Index, DateTimeOffset EnqueuedAt) : IQueueJob;
public sealed record DemoSleep2s(Guid BatchId, int Index, DateTimeOffset EnqueuedAt) : IQueueJob;
public sealed record DemoSleep10s(Guid BatchId, int Index, DateTimeOffset EnqueuedAt) : IQueueJob;
public sealed record DemoSleep30s(Guid BatchId, int Index, DateTimeOffset EnqueuedAt) : IQueueJob;

/// <summary>A schedule occurrence; the handler resolves the matching <see cref="CronTasks"/> method.</summary>
public sealed record ScheduledCronJob(string Name) : IQueueJob;
