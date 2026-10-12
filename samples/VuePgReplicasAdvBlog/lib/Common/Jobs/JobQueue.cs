using System.Text.Json;
using VuePgReplicasAdvBlog.Data;

namespace VuePgReplicasAdvBlog.Infrastructure;

// Job payloads. The record's type name is stored as Job.Type, the record as JSON in Job.Payload.
public interface IJob;

public interface IJobHandler<in T>
{
    Task HandleAsync(T payload, CancellationToken ct);
}

/// <summary>
/// Durable queue: rows in the Jobs table are the source of truth; independent workers poll for due rows.
/// <see cref="EnqueueAsync{T}"/> commits in the caller's transaction (outbox).
/// </summary>
public sealed class JobQueue(TimeProvider? clock = null)
{
    readonly TimeProvider time = clock ?? TimeProvider.System;
    public Job Enqueue<T>(AppDb db, T payload, DateTime? runAt = null) where T : notnull
    {
        var now = time.GetUtcNow().UtcDateTime;
        var job = new Job
        {
            Type = typeof(T).Name,
            Payload = JsonSerializer.Serialize(payload),
            Status = JobStatus.Pending,
            RunAt = runAt ?? now,
            CreatedAt = now,
        };
        db.Jobs.Add(job);
        return job;
    }

    /// <summary>Adds the job, saves it (committing anything else pending on <paramref name="db"/>).</summary>
    public async Task<Guid> EnqueueAsync<T>(AppDb db, T payload, DateTime? runAt = null, CancellationToken ct = default) where T : IJob
    {
        var job = Enqueue(db, payload, runAt);
        await db.SaveChangesAsync(ct);
        return job.Id;
    }
}
