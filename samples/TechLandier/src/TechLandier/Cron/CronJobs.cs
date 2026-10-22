using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NCrontab;
using TechLandier.Data;
using TickerQ.Utilities;

namespace TechLandier.Infrastructure;

public delegate Task CronTask(CronTasks tasks, CancellationToken ct);

/// Runs the schedule below through TickerQ. A database lease per job fences duplicate runs across replicas,
/// and several missed slots collapse into one run.
public sealed class CronJobs(IDbContextFactory<AppDb> dbs, IServiceScopeFactory scopes, ILogger<CronJobs> log)
{
    static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(30);

    internal static readonly (string Name, string Cron, CronTask Run)[] Schedule =
    [
        ("sales-rollup", "*/15 * * * *", async (t, ct) => await t.SalesRollupAsync(ct)),
        ("bnpl-reminders", "0 7 * * *", async (t, ct) => await t.BnplRemindersAsync(ct)),
        ("bnpl-overdue", "10 7 * * *", async (t, ct) => await t.BnplOverdueAsync(ct)),
        ("bnpl-statement", "0 8 * * 1", async (t, ct) => await t.BnplStatementAsync(ct)),
        ("ticket-escalation", "0 * * * *", async (t, ct) => await t.TicketEscalationAsync(ct)),
        ("order-timeout", "0 * * * *", async (t, ct) => await t.OrderTimeoutAsync(ct)),
        ("backup", "0 2 * * *", async (t, ct) => await t.BackupAsync(ct)),
        ("prune-product-views", "0 3 * * *", async (t, ct) => await t.PruneViewsAsync(ct)),
        ("orphan-assets", "0 * * * *", async (t, ct) => await t.OrphanAssetsAsync(ct)),
        ("prune-unverified", "0 4 * * *", async (t, ct) => await t.PruneUnverifiedAsync(ct)),
        ("prune-jobs", "30 3 * * *", async (t, ct) => await t.PruneJobsAsync(ct)),
    ];

    /// `Cron:<name>` (env CRON_<NAME>) overrides the default expression. Parsed eagerly so a typo fails startup, not 10s later.
    public static Dictionary<string, CrontabSchedule> Schedules(IConfiguration config) => Schedule.ToDictionary(s => s.Name, s =>
    {
        var text = config[$"Cron:{s.Name}"] is { Length: > 0 } c ? c : s.Cron;
        try { return CrontabSchedule.Parse(CronExpression.Parse(text).Value, new CrontabSchedule.ParseOptions { IncludingSeconds = true }); }
        catch (Exception e) when (e is ArgumentException or CrontabException) { throw new InvalidOperationException($"Invalid cron expression '{text}' for Cron:{s.Name}: {e.Message}", e); }
    });

    /// Runs the job when an occurrence is due and this process wins its lease. True means "not due yet, check again soon".
    internal async Task<bool> Tick((string Name, string Cron, CronTask Run) entry, CrontabSchedule expr, CancellationToken ct)
    {
        var name = entry.Name;
        var now = DateTime.UtcNow;
        DateTime due, lease;
        await using (var db = await dbs.CreateDbContextAsync(ct))
        {
            // A new schedule starts at "now", so it waits for its next occurrence instead of firing immediately.
            await db.Database.ExecuteSqlAsync($"INSERT INTO \"CronLeases\" (\"Name\", \"LastRunAt\", \"LockedUntil\") VALUES ({name}, {now}, NULL) ON CONFLICT (\"Name\") DO NOTHING", ct);
            var last = await db.CronLeases.Where(l => l.Name == name).Select(l => l.LastRunAt).FirstAsync(ct);
            DateTime? next = last is null ? null : expr.GetNextOccurrences(DateTime.SpecifyKind(last.Value, DateTimeKind.Utc), now.AddTicks(1)).LastOrDefault();
            if (next is not { } d || d == default) return false;
            due = d;

            lease = now.Add(LeaseTtl); // unique per claim: completion and release only match this exact value (fencing)
            var claimed = await db.CronLeases
                .Where(l => l.Name == name && (l.LockedUntil == null || l.LockedUntil < now) && (l.LastRunAt == null || l.LastRunAt < due))
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.LockedUntil, lease), ct);
            if (claimed == 0)
            {
                log.LogDebug("Cron {Name} occurrence {Due:u} already claimed or completed", name, due);
                return true;
            }
        }

        var finished = false;
        try
        {
            log.LogInformation("Cron {Name} running (due {Due:u})", name, due);
            await using var scope = scopes.CreateAsyncScope();
            var started = Stopwatch.GetTimestamp();
            await entry.Run(scope.ServiceProvider.GetRequiredService<CronTasks>(), ct);
            finished = true;
            log.LogInformation("Cron {Name} completed in {ElapsedMs:F1} ms", name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            log.LogWarning("Cron {Name} interrupted by shutdown; it will run again on next start", name);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Cron {Name} failed; waiting for the next occurrence", name);
            finished = true;
        }
        finally
        {
            // Not the stopping token: the lease must be released even while shutting down.
            await using var db = await dbs.CreateDbContextAsync();
            var held = db.CronLeases.Where(l => l.Name == name && l.LockedUntil == lease);
            if (finished) await held.ExecuteUpdateAsync(s => s.SetProperty(l => l.LastRunAt, due).SetProperty(l => l.LockedUntil, (DateTime?)null));
            else await held.ExecuteUpdateAsync(s => s.SetProperty(l => l.LockedUntil, (DateTime?)null));
        }
        return false;
    }
}
