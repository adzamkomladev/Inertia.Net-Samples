using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Runwick;
using Runwick.Postgres;
using TechLandier.Data;

namespace TechLandier.Infrastructure;

// Runwick job contracts. Each record maps to one stable job name in RunwickSetup; payloads carry ids, never rendered content.

/// <summary>The verification link is generated when the job runs, so a retried job never mails an expired token.</summary>
public sealed record SendVerificationEmail(Guid UserId) : IQueueJob;
public sealed record OrderPaidEmail(Guid OrderId) : IQueueJob;
public sealed record OrderShippedEmail(Guid OrderId) : IQueueJob;
public sealed record OrderCancelledEmail(Guid OrderId, string Reason) : IQueueJob;
public sealed record OrderRefundEmail(Guid OrderId) : IQueueJob;
public sealed record InstallmentPaidEmail(Guid InstallmentId) : IQueueJob;
/// <summary><paramref name="Kind"/> is reminder or overdue; <paramref name="Day"/> (yyyy-MM-dd) makes the uniqueness key per plan, kind and day.</summary>
public sealed record InstallmentReminderEmail(Guid PlanId, string Kind, string Day) : IQueueJob;
/// <summary>The weekly statement lists every open installment of every active plan of one user.</summary>
public sealed record InstallmentStatementEmail(Guid UserId, string Day) : IQueueJob;
public sealed record TicketReplyEmail(Guid TicketId) : IQueueJob;
/// <summary>Kind from CareService: warranty-claim | insurance-claim | repair.</summary>
public sealed record CareDecisionEmail(string Kind, Guid Id) : IQueueJob;
public sealed record DeleteAssets(string[] Keys) : IQueueJob;
public sealed record RealtimePush(string Method, System.Text.Json.JsonElement Payload, string? Group = null, string? UserId = null) : IQueueJob;

/// <summary>A schedule occurrence; the handler resolves the matching <see cref="CronTasks"/> method.</summary>
public sealed record ScheduledCronJob(string Name) : IQueueJob;

/// <summary>
/// Where features hand work to Runwick. <see cref="StageAsync{T}"/> writes the job inside the caller's open EF
/// transaction (the Runwick tables live in the same database), so it commits or rolls back with the business rows.
/// <see cref="DispatchAsync{T}"/> is for work decided without a business write, such as realtime fan-out.
/// </summary>
public interface IJobOutbox
{
    Task StageAsync<T>(AppDb db, T job, CancellationToken ct = default) where T : class, IQueueJob;
    Task StageManyAsync(AppDb db, IReadOnlyList<IQueueJob> jobs, CancellationToken ct = default);
    Task DispatchAsync<T>(T job, CancellationToken ct = default) where T : class, IQueueJob;
    Task DispatchManyAsync(IReadOnlyList<IQueueJob> jobs, CancellationToken ct = default);
}

public sealed class RunwickJobOutbox(IPostgresJobDispatcher staged, IJobDispatcher direct) : IJobOutbox
{
    static readonly DispatchOptions Defaults = new();
    /// <summary>Runwick refuses batches above 100 items.</summary>
    const int MaxBatch = 100;

    public async Task StageAsync<T>(AppDb db, T job, CancellationToken ct = default) where T : class, IQueueJob =>
        await staged.StageAsync(job, Transaction(db), Defaults, ct);

    /// <summary>Duplicates of a uniqueness key are reused rather than failing the business transaction.</summary>
    public async Task StageManyAsync(AppDb db, IReadOnlyList<IQueueJob> jobs, CancellationToken ct = default)
    {
        if (jobs.Count == 0) return;
        var tx = Transaction(db);
        foreach (var chunk in jobs.Chunk(MaxBatch))
            await staged.StageManyAsync(chunk.Select(j => new BatchDispatchItem(j, Defaults)).ToList(), tx,
                new BatchDispatchOptions { ContinueOnConflict = true }, ct);
    }

    static NpgsqlTransaction Transaction(AppDb db) =>
        db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction
            ?? throw new InvalidOperationException("Stage jobs inside db.Database.BeginTransactionAsync(); use DispatchAsync when nothing is written.");

    public async Task DispatchAsync<T>(T job, CancellationToken ct = default) where T : class, IQueueJob =>
        await direct.DispatchAsync(job, Defaults, ct);

    public async Task DispatchManyAsync(IReadOnlyList<IQueueJob> jobs, CancellationToken ct = default)
    {
        foreach (var chunk in jobs.Chunk(MaxBatch))
            await direct.DispatchManyAsync(chunk.Select(j => new BatchDispatchItem(j, Defaults)).ToList(),
                new BatchDispatchOptions { ContinueOnConflict = true }, ct);
    }
}
