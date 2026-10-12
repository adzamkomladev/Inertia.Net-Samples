using Microsoft.EntityFrameworkCore;
using NCrontab;
using ReactSqliteReplicasBlog.Data;

namespace ReactSqliteReplicasBlog.Cron;

/// <summary>One database claim per scheduled occurrence, shared by all app processes.</summary>
public static class CronLeaseRunner
{
    internal static async Task RunAsync(CronTask task, string expression, IServiceProvider services, CancellationToken ct)
    {
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var schedule = CrontabSchedule.Parse(expression, new CrontabSchedule.ParseOptions { IncludingSeconds = true });
        var due = LatestDue(schedule, now);
        if (due == default) return;
        var scopes = services.GetRequiredService<IServiceScopeFactory>();
        await using var claimScope = scopes.CreateAsyncScope();
        var db = claimScope.ServiceProvider.GetRequiredService<AppDb>();
        var name = task.Name;
        await db.Database.ExecuteSqlAsync($"INSERT OR IGNORE INTO CronLeases (Name) VALUES ({name})", ct);
        var token = Guid.NewGuid().ToString("N");
        var expires = now.AddMinutes(30);
        var claimed = await db.CronLeases.Where(l => l.Name == name && (l.LockedUntil == null || l.LockedUntil < now)
                && (l.LastRunAt == null || l.LastRunAt < due))
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.LockedUntil, expires).SetProperty(l => l.Token, token), ct);
        if (claimed == 0) return;
        var finished = false;
        try
        {
            await using var runScope = scopes.CreateAsyncScope();
            await task.Run(runScope.ServiceProvider, ct);
            finished = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            // Match the existing scheduler: a failed occurrence waits until the next slot.
            finished = true;
            throw;
        }
        finally
        {
            await using var releaseScope = scopes.CreateAsyncScope();
            var releaseDb = releaseScope.ServiceProvider.GetRequiredService<AppDb>();
            var lease = releaseDb.CronLeases.Where(l => l.Name == name && l.Token == token);
            if (finished) await lease.ExecuteUpdateAsync(s => s.SetProperty(l => l.LastRunAt, due)
                .SetProperty(l => l.LockedUntil, (DateTime?)null).SetProperty(l => l.Token, (string?)null));
            else await lease.ExecuteUpdateAsync(s => s.SetProperty(l => l.LockedUntil, (DateTime?)null).SetProperty(l => l.Token, (string?)null));
        }
    }

    internal static DateTime LatestDue(CrontabSchedule schedule, DateTime now)
    {
        // Search the boundary before the last occurrence, without enumerating millions of
        // second-level ticks. Eight years includes annual and leap-day cron overrides.
        var lower = now.AddYears(-8).Ticks;
        var upper = now.Ticks;
        var end = now.AddTicks(1);
        var due = default(DateTime);
        while (lower <= upper)
        {
            var middle = lower + (upper - lower) / 2;
            var next = schedule.GetNextOccurrence(new DateTime(middle, DateTimeKind.Utc), end);
            if (next < end) { due = next; lower = middle + 1; }
            else upper = middle - 1;
        }
        return due;
    }
}
