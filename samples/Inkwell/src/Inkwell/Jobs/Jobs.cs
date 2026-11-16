using Inkwell.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Storage;
using Runwick;
using Runwick.Sqlite;

namespace Inkwell.Jobs;

// Runwick job contracts. Each record maps to one stable job name in RunwickSetup; payloads carry ids, never content.
public sealed record SendVerificationEmail(Guid UserId) : IQueueJob;
public sealed record CommentNotification(Guid CommentId) : IQueueJob;
/// <summary>
/// One job per essay per fixed <see cref="Delay"/> window sums up that window's unread likes ("Ann and 3 others liked …").
/// The job runs <see cref="Delay"/> after the first like, which is always after its window has closed, so no like can
/// arrive in a window whose job is already running; the next window gets its own job.
/// </summary>
public sealed record LikeNotification(Guid PostId, long Window) : IQueueJob
{
    public static readonly TimeSpan Delay = TimeSpan.FromMinutes(15);
    public static LikeNotification For(Guid postId, DateTime utcNow) => new(postId, utcNow.Ticks / Delay.Ticks);
    public DateTime WindowStart => new(Window * Delay.Ticks, DateTimeKind.Utc);
    public DateTime WindowEnd => WindowStart + Delay;
}
/// <summary><paramref name="Week"/> is the ISO year and week (yyyyww); with the user it forms the uniqueness key.</summary>
public sealed record WeeklyDigestEmail(Guid UserId, int Week) : IQueueJob;

/// <summary>
/// Where features hand work to Runwick. <see cref="StageAsync{T}"/> writes the job inside the caller's open EF
/// transaction (the Runwick tables live in the same SQLite file), so it commits or rolls back with the business rows.
/// <see cref="DispatchAsync{T}"/> is for work decided after a commit, such as realtime fan-out.
/// </summary>
public interface IJobOutbox
{
    Task StageAsync<T>(AppDb db, T job, CancellationToken ct = default, TimeSpan? delay = null) where T : class, IQueueJob;
    Task StageManyAsync(AppDb db, IReadOnlyList<IQueueJob> jobs, CancellationToken ct = default);
    Task DispatchAsync<T>(T job, CancellationToken ct = default) where T : class, IQueueJob;
    Task DispatchManyAsync(IReadOnlyList<IQueueJob> jobs, CancellationToken ct = default);
}

public sealed class RunwickJobOutbox(ISqliteJobDispatcher staged, IJobDispatcher direct) : IJobOutbox
{
    static readonly DispatchOptions Defaults = new();

    public async Task StageAsync<T>(AppDb db, T job, CancellationToken ct = default, TimeSpan? delay = null) where T : class, IQueueJob
    {
        // EF's SQLite transactions begin IMMEDIATE, which is what Runwick asks for when staging.
        var options = delay is { } d ? new DispatchOptions { NotBefore = DateTimeOffset.UtcNow + d } : Defaults;
        await staged.StageAsync(job, Transaction(db), options, ct);
    }

    public async Task StageManyAsync(AppDb db, IReadOnlyList<IQueueJob> jobs, CancellationToken ct = default)
    {
        if (jobs.Count == 0) return;
        var tx = Transaction(db);
        await staged.StageManyAsync(jobs.Select(j => new BatchDispatchItem(j, Defaults)).ToList(), tx, new BatchDispatchOptions(), ct);
    }

    static SqliteTransaction Transaction(AppDb db) =>
        db.Database.CurrentTransaction?.GetDbTransaction() as SqliteTransaction
            ?? throw new InvalidOperationException("Stage jobs inside db.Database.BeginTransactionAsync(); use DispatchAsync after a commit.");

    public async Task DispatchAsync<T>(T job, CancellationToken ct = default) where T : class, IQueueJob =>
        await direct.DispatchAsync(job, Defaults, ct);

    public async Task DispatchManyAsync(IReadOnlyList<IQueueJob> jobs, CancellationToken ct = default)
    {
        foreach (var chunk in jobs.Chunk(500))
            await direct.DispatchManyAsync(chunk.Select(j => new BatchDispatchItem(j, Defaults)).ToList(),
                new BatchDispatchOptions { ContinueOnConflict = true }, ct);
    }
}

/// <summary>Used when Runwick is switched off (tests replace it): background work cannot be accepted.</summary>
public sealed class DisabledJobOutbox : IJobOutbox
{
    static InvalidOperationException Off() => new("Runwick is disabled (Runwick:Enabled=false); background jobs cannot be dispatched.");
    public async Task StageAsync<T>(AppDb db, T job, CancellationToken ct = default, TimeSpan? delay = null) where T : class, IQueueJob => await Task.FromException(Off());
    public async Task StageManyAsync(AppDb db, IReadOnlyList<IQueueJob> jobs, CancellationToken ct = default) => await Task.FromException(Off());
    public async Task DispatchAsync<T>(T job, CancellationToken ct = default) where T : class, IQueueJob => await Task.FromException(Off());
    public async Task DispatchManyAsync(IReadOnlyList<IQueueJob> jobs, CancellationToken ct = default) => await Task.FromException(Off());
}
