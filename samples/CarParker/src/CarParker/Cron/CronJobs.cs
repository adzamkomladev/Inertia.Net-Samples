using NCrontab;
using TickerQ.Utilities;
using System.Diagnostics;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Analytics;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
using CarParker.Features.Insurance;

namespace CarParker.Infrastructure;

/// <summary>TickerQ jobs retain database leases to fence duplicate runs across replicas.</summary>
public sealed class CronJobs(AppDb db, IServiceScopeFactory scopes, ILogger<CronJobs> log)
{
    static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(30);
    internal static readonly (string Name, string Cron, Func<IServiceProvider, CancellationToken, Task> Run)[] Schedule =
    [
        ("prune-jobs", "30 3 * * *", PruneJobs),
        ("orphan-assets", "0 * * * *", OrphanAssets),
        ("prune-unverified", "0 4 * * *", PruneUnverified),
        ("lots-stat-rollup", "*/15 * * * *", async (sp, ct) => await sp.GetRequiredService<LotAnalyticsService>().RollupAsync(ct)),
        ("reservation-sweeper", "5 7 * * *", ReservationSweeper),
        ("no-show-check", "*/10 * * * *", async (sp, ct) => await sp.GetRequiredService<GuardService>().SweepNoShowsAsync(ct)),
        ("overstay-check", "*/10 * * * *", async (sp, ct) => await sp.GetRequiredService<GuardService>().FlagOverstaysAsync(ct)),
        ("policy-expiry", "0 4 * * *", async (sp, ct) => await sp.GetRequiredService<InsuranceService>().ExpireOverdueAsync(ct)),
    ];

    public static Dictionary<string, CrontabSchedule> Schedules(IConfiguration config) => Schedule.ToDictionary(s => s.Name, s =>
    {
        var text = config[$"Cron:{s.Name}"] is { Length: > 0 } c ? c : s.Cron;
        try { return CrontabSchedule.Parse(CronExpression.Parse(text).Value, new CrontabSchedule.ParseOptions { IncludingSeconds = true }); }
        catch (Exception e) when (e is ArgumentException or CrontabException) { throw new InvalidOperationException($"Invalid cron expression '{text}' for Cron:{s.Name}: {e.Message}", e); }
    });

    /// <summary>Runs the entry when its schedule is due and no replica holds the lease. Returns true when a held lease should be rechecked soon.</summary>
    internal async Task<bool> Tick((string Name, string Cron, Func<IServiceProvider, CancellationToken, Task> Run) entry, CrontabSchedule expr, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await db.CronLeases.UpdateOneAsync(x => x.Name == entry.Name,
            Builders<CronLease>.Update.SetOnInsert(x => x.Name, entry.Name).SetOnInsert(x => x.LastRunAt, now), new UpdateOptions { IsUpsert = true }, ct);
        var leaseDoc = await db.CronLeases.Find(x => x.Name == entry.Name).FirstAsync(ct);
        var due = leaseDoc.LastRunAt is null ? (DateTime?)null
            : expr.GetNextOccurrences(DateTime.SpecifyKind(leaseDoc.LastRunAt.Value, DateTimeKind.Utc), now.AddTicks(1)).LastOrDefault();
        if (due is not { } occurrence || occurrence == default) return false;

        var lease = now.Add(LeaseTtl);
        var claimed = await db.CronLeases.FindOneAndUpdateAsync(
            x => x.Name == entry.Name && (x.LockedUntil == null || x.LockedUntil < now) && (x.LastRunAt == null || x.LastRunAt < occurrence),
            Builders<CronLease>.Update.Set(x => x.LockedUntil, lease), new FindOneAndUpdateOptions<CronLease> { ReturnDocument = ReturnDocument.After }, ct);
        if (claimed is null) return true;

        var finished = false;
        try
        {
            log.LogInformation("Cron {Name} running (due {Due:u})", entry.Name, occurrence);
            await using var runScope = scopes.CreateAsyncScope();
            await entry.Run(runScope.ServiceProvider, ct);
            finished = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e) { log.LogError(e, "Cron {Name} failed; waiting for next occurrence", entry.Name); finished = true; }
        finally
        {
            var release = Builders<CronLease>.Update.Set(x => x.LockedUntil, null);
            if (finished) release = release.Set(x => x.LastRunAt, occurrence);
            await db.CronLeases.UpdateOneAsync(x => x.Name == entry.Name && x.LockedUntil == lease, release, cancellationToken: CancellationToken.None);
        }
        return false;
    }

    static async Task OrphanAssets(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var queue = sp.GetRequiredService<JobQueue>();
        var pendingBefore = DateTime.UtcNow.AddHours(-1);
        var readyBefore = DateTime.UtcNow.AddHours(-24);
        for (var i = 0; i < 50; i++)
        {
            var candidates = await db.Assets.Find(x => (x.Status == AssetStatus.Pending && x.CreatedAt < pendingBefore) || (x.Status == AssetStatus.Ready && x.CreatedAt < readyBefore))
                .SortBy(x => x.Id).Limit(200).ToListAsync(ct);
            if (candidates.Count == 0) return;
            var ids = candidates.Select(x => x.Id).ToArray();
            // Avatars are the only uploads in this domain.
            var referenced = await db.Users.Find(x => x.AvatarAssetId != null && ids.Contains(x.AvatarAssetId.Value)).Project(x => x.AvatarAssetId!.Value).ToListAsync(ct);
            var remove = candidates.Where(x => !referenced.Contains(x.Id)).ToArray();
            if (remove.Length == 0) return;
            var removeIds = remove.Select(x => x.Id).ToArray();
            await db.TransactionAsync(async session =>
            {
                await db.Assets.DeleteManyAsync(session, x => removeIds.Contains(x.Id), cancellationToken: ct);
                await queue.EnqueueAsync(db, new DeleteAssets(remove.Select(x => x.Key).ToArray()), session: session, ct: ct);
            }, ct);
            queue.Wake();
        }
    }

    /// <summary>Deletes accounts left unverified for a week, unless they have booking history.</summary>
    static async Task PruneUnverified(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var ids = await db.Users.Find(u => u.EmailVerifiedAt == null && u.CreatedAt < cutoff).Limit(500).Project(u => u.Id).ToListAsync(ct);
        var withBookings = await db.Reservations.Distinct(r => r.UserId, r => ids.Contains(r.UserId)).ToListAsync(ct);
        var removable = ids.Except(withBookings).ToArray();
        await db.Users.DeleteManyAsync(u => removable.Contains(u.Id) && u.EmailVerifiedAt == null, ct);
    }

    static async Task PruneJobs(IServiceProvider sp, CancellationToken ct)
    {
        var done = DateTime.UtcNow.AddDays(-7);
        var failed = DateTime.UtcNow.AddDays(-30);
        await sp.GetRequiredService<AppDb>().Jobs.DeleteManyAsync(x => (x.Status == JobStatus.Done && x.CreatedAt < done) || (x.Status == JobStatus.Failed && x.CreatedAt < failed), ct);
    }

    /// <summary>Daily sweep of tomorrow's windows: queues one reminder per occurrence. The reminder handler writes an alert
    /// marked with the occurrence id after emailing, and that marker keeps the next sweep from queuing it again.</summary>
    static async Task ReservationSweeper(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var from = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(1), DateTimeKind.Utc);
        var to = from.AddDays(1);
        var upcoming = await db.Occurrences.Find(o => o.Status == OccurrenceStatus.Scheduled && o.StartAtUtc >= from && o.StartAtUtc < to)
            .Project(o => new { o.Id, o.ReservationId }).ToListAsync(ct);
        if (upcoming.Count == 0) return;
        var reservationIds = upcoming.Select(o => o.ReservationId).Distinct().ToArray();
        var confirmed = (await db.Reservations.Distinct(r => r.Id, r => reservationIds.Contains(r.Id) && r.Status == ReservationStatus.Confirmed).ToListAsync(ct)).ToHashSet();
        var markers = upcoming.Select(o => $"occurrence:{o.Id}").ToArray();
        var swept = (await db.Alerts.Find(a => markers.Contains(a.Body!)).Project(a => a.Body).ToListAsync(ct)).ToHashSet();
        var reminders = upcoming.Where(o => confirmed.Contains(o.ReservationId) && !swept.Contains($"occurrence:{o.Id}"))
            .Select(o => new ReservationReminderEmail(o.ReservationId, o.Id)).ToList();
        await sp.GetRequiredService<JobQueue>().EnqueueManyAsync(db, reminders, ct);
    }

}
