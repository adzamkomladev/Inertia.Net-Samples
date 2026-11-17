using Runwick;

namespace CarParker.Infrastructure;

// Runwick job contracts. Each record maps to one stable job name in RunwickSetup; payloads carry ids, never rendered content.
// Runwick's PostgreSQL store cannot share a transaction with MongoDB: services enqueue right after the business write commits,
// and every handler tolerates a repeat (uniqueness keys on dispatch, idempotent writes in the handler).

/// <summary>The verification link is minted when the job runs, so a retried job never mails an expired token.</summary>
public sealed record SendVerificationEmail(Guid UserId) : IQueueJob;
/// <summary>"Your parking session ended" mail; the session id is the idempotency key.</summary>
public sealed record SessionEndedEmail(Guid SessionId) : IQueueJob;
/// <summary>The reset link is minted when the job runs (60 minutes from then), like the verification link.</summary>
public sealed record SendPasswordResetEmail(Guid UserId) : IQueueJob;
/// <summary>"Your window was released" mail after the no-show sweep; the occurrence id is the idempotency key.</summary>
public sealed record NoShowEmail(Guid OccurrenceId) : IQueueJob;
/// <summary>"Your car is still parked past its window" mail from the overstay sweep; the session id is the idempotency key.</summary>
public sealed record OverstayEmail(Guid SessionId) : IQueueJob;
public sealed record RefundPaidEmail(Guid RefundId) : IQueueJob;
/// <summary>"Your bay is booked" mail with the dates, total and receipt link; the reservation id is the idempotency key.</summary>
public sealed record BookingConfirmedEmail(Guid ReservationId) : IQueueJob;
public sealed record ReservationReminderEmail(Guid ReservationId, Guid OccurrenceId) : IQueueJob;
/// <summary><paramref name="Decision"/> is paid or rejected.</summary>
public sealed record ClaimDecisionEmail(Guid ClaimId, string Decision) : IQueueJob;
public sealed record DeleteAssets(string[] Keys) : IQueueJob;
public sealed record RealtimePush(string Method, System.Text.Json.JsonElement Payload, string? Group = null, string? UserId = null) : IQueueJob;

/// <summary>A schedule occurrence; the handler resolves the matching <see cref="CronJobs"/> entry.</summary>
public sealed record ScheduledCronJob(string Name) : IQueueJob;

/// <summary>Where features hand work to Runwick. Call it after the business write has committed.</summary>
public sealed class JobEnqueuer(IJobDispatcher dispatcher)
{
    static readonly DispatchOptions Defaults = new();
    /// <summary>Runwick refuses batches above 100 items.</summary>
    const int MaxBatch = 100;

    /// <summary>A repeat of a uniqueness key is reused, not an error: callers may retry after a lost response.</summary>
    public async Task DispatchAsync<T>(T job, CancellationToken ct = default) where T : class, IQueueJob =>
        await DispatchManyAsync([job], ct);

    /// <summary>Duplicates of a uniqueness key are reused rather than failing the batch.</summary>
    public async Task DispatchManyAsync(IReadOnlyList<IQueueJob> jobs, CancellationToken ct = default)
    {
        foreach (var chunk in jobs.Chunk(MaxBatch))
            await dispatcher.DispatchManyAsync(chunk.Select(j => new BatchDispatchItem(j, Defaults)).ToList(),
                new BatchDispatchOptions { ContinueOnConflict = true }, ct);
    }
}
