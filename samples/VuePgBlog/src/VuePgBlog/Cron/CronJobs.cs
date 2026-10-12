using System.Diagnostics;
using Npgsql;
using NCrontab;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;
using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;

namespace VuePgBlog.Infrastructure;

/// <summary>TickerQ jobs retain database leases to fence duplicate runs across replicas.</summary>
public sealed class CronJobs(IServiceScopeFactory scopes, ILogger<CronJobs> log)
{
    static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(30);

    internal static readonly (string Name, string Cron, Func<IServiceProvider, CancellationToken, Task> Run)[] Schedule =
    [
        ("analytics-rollup", "*/15 * * * *", Rollup),
        ("backup", "0 2 * * *", Backup),
        ("prune-views", "0 3 * * *", PruneViews),
        ("orphan-assets", "0 * * * *", OrphanAssets),
        ("prune-unverified", "0 4 * * *", PruneUnverified),
        ("prune-jobs", "30 3 * * *", PruneJobs),
        ("weekly-digest", "0 8 * * 1", WeeklyDigest),
        ("recommendation-digest", "*/5 * * * *", async (sp, ct) =>
            { await sp.GetRequiredService<DigestService>().EnqueueDueAsync(sp.GetRequiredService<AppDb>(), sp.GetRequiredService<JobQueue>(), ct); }),
        ("search-reconcile", "*/5 * * * *", async (sp, ct) =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                if (string.Equals(config["SEARCH_DRIVER"], "meilisearch", StringComparison.OrdinalIgnoreCase))
                    await SearchIndexing.ReconcileAsync(sp.GetRequiredService<AppDb>(), sp.GetRequiredService<JobQueue>(), ct);
            }),
        ("interest-profiles", "*/5 * * * *", async (sp, ct) =>
            { await sp.GetRequiredService<VuePgBlog.Features.Discovery.DiscoveryService>().RefreshProfilesAsync(
                sp.GetRequiredService<AppDb>(), sp.GetRequiredService<JobQueue>(), ct); }),

    ];

    /// `Cron:<name>` (env CRON_<NAME>) overrides the default expression. Parsed eagerly so a typo fails startup, not 10s later.
    public static Dictionary<string, CrontabSchedule> Schedules(IConfiguration config) => Schedule.ToDictionary(s => s.Name, s =>
    {
        var text = config[$"Cron:{s.Name}"] is { Length: > 0 } c ? c : s.Cron;
        try { return CrontabSchedule.Parse(CronExpression.Parse(text).Value, new CrontabSchedule.ParseOptions { IncludingSeconds = true }); }
        catch (Exception e) when (e is ArgumentException or CrontabException) { throw new InvalidOperationException($"Invalid cron expression '{text}' for Cron:{s.Name}: {e.Message}", e); }
    });

    internal async Task<bool> Tick((string Name, string Cron, Func<IServiceProvider, CancellationToken, Task> Run) entry, CrontabSchedule expr, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var name = entry.Name;
        var now = DateTime.UtcNow;

        // New schedule: baseline at "now" so it waits for its next occurrence instead of firing immediately.
        await db.Database.ExecuteSqlAsync($"INSERT INTO \"CronLeases\" (\"Name\", \"LastRunAt\", \"LockedUntil\") VALUES ({name}, {now}, NULL) ON CONFLICT (\"Name\") DO NOTHING", ct);
        var last = await db.CronLeases.AsNoTracking().Where(l => l.Name == name).Select(l => l.LastRunAt).FirstAsync(ct);

        // Latest occurrence that is already due; several missed slots collapse into one run.
        var due = last is null ? (DateTime?)null : expr.GetNextOccurrences(DateTime.SpecifyKind(last.Value, DateTimeKind.Utc), now.AddTicks(1)).LastOrDefault();
        if (due is not { } d || d == default) return false;

        var lease = now.Add(LeaseTtl); // unique per claim; completion/release only match this exact value (fencing)
        var claimed = await db.CronLeases
            .Where(l => l.Name == name && (l.LockedUntil == null || l.LockedUntil < now) && (l.LastRunAt == null || l.LastRunAt < d))
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.LockedUntil, lease), ct);
        if (claimed == 0)
        {
            log.LogDebug("Cron {Name} occurrence {Due:u} already claimed or completed", name, d);
            return true;
        }

        var finished = false;
        try
        {
            log.LogInformation("Cron {Name} running (due {Due:u})", name, d);
            await using var runScope = scopes.CreateAsyncScope();
            var started = Stopwatch.GetTimestamp();
            await entry.Run(runScope.ServiceProvider, ct);
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
            // Never pass the stopping token here: the lease must be released even while shutting down.
            await using var s2 = scopes.CreateAsyncScope();
            var db2 = s2.ServiceProvider.GetRequiredService<AppDb>();
            var q = db2.CronLeases.Where(l => l.Name == name && l.LockedUntil == lease);
            if (finished) await q.ExecuteUpdateAsync(s => s.SetProperty(l => l.LastRunAt, d).SetProperty(l => l.LockedUntil, (DateTime?)null));
            else await q.ExecuteUpdateAsync(s => s.SetProperty(l => l.LockedUntil, (DateTime?)null));
        }
        return false;
    }

    // Custom-format archives are isolated by project and deployment environment in the shared bucket.
    static async Task Backup(IServiceProvider sp, CancellationToken ct)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var db = sp.GetRequiredService<AppDb>();
        var storage = sp.GetRequiredService<Storage>();
        var now = sp.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var environment = sp.GetRequiredService<IHostEnvironment>().EnvironmentName;
        var segment = string.Concat(environment.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-')).ToLowerInvariant();
        var prefix = $"backups/VuePgBlog/{segment}/";
        var bucket = config["Storage:BackupBucket"] ?? config["Storage:Bucket"] ?? "uploads";
        var path = Path.Combine(Path.GetTempPath(), $"VuePgBlog-{Guid.NewGuid():N}.dump");
        var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        var start = new ProcessStartInfo(config["PG_DUMP_PATH"] ?? "pg_dump")
        {
            UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var arg in new[] { "--format=custom", "--no-password", "--file", path }) start.ArgumentList.Add(arg);
        start.Environment["PGHOST"] = connection.Host;
        start.Environment["PGPORT"] = connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["PGDATABASE"] = connection.Database;
        start.Environment["PGUSER"] = connection.Username;
        start.Environment["PGPASSWORD"] = connection.Password;
        start.Environment["PGSSLMODE"] = connection.SslMode switch
        {
            SslMode.VerifyCA => "verify-ca", SslMode.VerifyFull => "verify-full",
            _ => connection.SslMode.ToString().ToLowerInvariant()
        };
        if (!string.IsNullOrEmpty(connection.RootCertificate)) start.Environment["PGSSLROOTCERT"] = connection.RootCertificate;
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start pg_dump.");
            var errors = process.StandardError.ReadToEndAsync(ct);
            try { await process.WaitForExitAsync(ct); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            if (process.ExitCode != 0) throw new InvalidOperationException($"pg_dump failed: {await errors}");
            await errors;
            await storage.UploadFileAsync(bucket, $"{prefix}{now:yyyy-MM-dd-HHmmss}-{Guid.NewGuid():N}.dump", path, ct);
        }
        finally { File.Delete(path); }
        var keys = await storage.ListKeysAsync(prefix, ct, bucket);
        var expired = keys.OrderByDescending(k => k, StringComparer.Ordinal).Skip(14).ToArray();
        if (expired.Length > 0) await storage.DeleteAsync(expired, ct, bucket);
    }

    // Recomputes today and yesterday from the raw tables; safe to run any number of times.
    static async Task Rollup(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var day in new[] { today.AddDays(-1), today })
        {
            var from = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var to = from.AddDays(1);
            var date = day;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Zero first so un-likes / deleted comments don't leave stale counts behind the upsert.
            await db.Database.ExecuteSqlAsync($"UPDATE \"PostDailyStats\" SET \"Views\" = 0, \"UniqueViewers\" = 0, \"Likes\" = 0, \"Comments\" = 0 WHERE \"Date\" = {date}", ct);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "PostDailyStats" ("PostId", "Date", "Views", "UniqueViewers", "Likes", "Comments")
                SELECT a."PostId", {date}, SUM(a."V")::integer, SUM(a."U")::integer, SUM(a."L")::integer, SUM(a."C")::integer
                FROM (
                    SELECT "PostId", COUNT(*) AS "V", COUNT(DISTINCT "ViewerHash") AS "U", 0 AS "L", 0 AS "C" FROM "PostViews" WHERE "At" >= {from} AND "At" < {to} GROUP BY "PostId"
                    UNION ALL
                    SELECT "PostId", 0, 0, COUNT(*), 0 FROM "Likes" WHERE "CreatedAt" >= {from} AND "CreatedAt" < {to} GROUP BY "PostId"
                    UNION ALL
                    SELECT "PostId", 0, 0, 0, COUNT(*) FROM "Comments" WHERE "CreatedAt" >= {from} AND "CreatedAt" < {to} GROUP BY "PostId"
                ) a JOIN "Posts" p ON p."Id" = a."PostId"
                GROUP BY a."PostId"
                ON CONFLICT ("PostId", "Date") DO UPDATE SET
                    "Views" = EXCLUDED."Views", "UniqueViewers" = EXCLUDED."UniqueViewers", "Likes" = EXCLUDED."Likes", "Comments" = EXCLUDED."Comments"
                """, ct);
            await tx.CommitAsync(ct);
        }
    }

    static async Task PruneViews(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var cutoff = DateTime.UtcNow.AddDays(-90);
        int n;
        do
        {
            n = await db.Database.ExecuteSqlAsync($"DELETE FROM \"PostViews\" WHERE \"Id\" IN (SELECT \"Id\" FROM \"PostViews\" WHERE \"At\" < {cutoff} ORDER BY \"Id\" LIMIT 5000)", ct);
            await Task.Delay(50, ct); // let request-path writers get the lock between chunks
        } while (n == 5000);
    }

    static async Task OrphanAssets(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var queue = sp.GetRequiredService<JobQueue>();
        var pendingBefore = DateTime.UtcNow.AddHours(-1);
        var readyBefore = DateTime.UtcNow.AddHours(-24);
        for (var i = 0; i < 50; i++) // safety cap; the rest goes next hour
        {
            // Rows go first, with the reference re-check in the same statement, so an asset that got attached meanwhile survives.
            // The object deletion is a durable DeleteAssets job committed in the same transaction, so an S3 outage only delays it.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var keys = await db.Database.SqlQuery<string>($"""
                WITH candidates AS (
                    SELECT a."Id" FROM "Assets" a
                    WHERE (a."Status" = 'Pending' AND a."CreatedAt" < {pendingBefore})
                        OR (a."Status" = 'Ready' AND a."CreatedAt" < {readyBefore}
                            AND NOT EXISTS (SELECT 1 FROM "Posts" p WHERE p."CoverAssetId" = a."Id")
                            AND NOT EXISTS (SELECT 1 FROM "Users" u WHERE u."AvatarAssetId" = a."Id"))
                    ORDER BY a."Id" LIMIT 200 FOR UPDATE SKIP LOCKED
                )
                DELETE FROM "Assets" a USING candidates c
                WHERE a."Id" = c."Id"
                    AND ((a."Status" = 'Pending' AND a."CreatedAt" < {pendingBefore})
                        OR (a."Status" = 'Ready' AND a."CreatedAt" < {readyBefore}
                            AND NOT EXISTS (SELECT 1 FROM "Posts" p WHERE p."CoverAssetId" = a."Id")
                            AND NOT EXISTS (SELECT 1 FROM "Users" u WHERE u."AvatarAssetId" = a."Id")))
                RETURNING a."Key" AS "Value"
                """).ToListAsync(ct);
            if (keys.Count == 0) return;
            await queue.EnqueueAsync(db, new DeleteAssets([.. keys]), ct: ct);
            await tx.CommitAsync(ct);
        }
    }

    static async Task PruneUnverified(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var cutoff = DateTime.UtcNow.AddDays(-7);
        await db.Users.Where(u => u.EmailVerifiedAt == null && u.CreatedAt < cutoff
            && !db.Posts.IgnoreQueryFilters().Any(p => p.AuthorId == u.Id) && !db.Comments.IgnoreQueryFilters().Any(c => c.UserId == u.Id)).ExecuteDeleteAsync(ct);
    }

    static async Task PruneJobs(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var done = DateTime.UtcNow.AddDays(-7);
        var failed = DateTime.UtcNow.AddDays(-30);
        await db.Jobs.Where(j => (j.Status == JobStatus.Done && j.CreatedAt < done) || (j.Status == JobStatus.Failed && j.CreatedAt < failed)).ExecuteDeleteAsync(ct);
    }

    static async Task WeeklyDigest(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var queue = sp.GetRequiredService<JobQueue>();
        var baseUrl = sp.GetRequiredService<IConfiguration>().AppBaseUrl() ?? "";
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-6);

        var users = await db.Users.AsNoTracking().Where(u => u.EmailVerifiedAt != null && u.Preferences.WeeklyDigest)
            .Select(u => new { u.Id, u.Email, u.Name }).ToListAsync(ct);
        var ids = users.Select(u => u.Id).ToList();
        var rows = (await (
            from s in db.PostDailyStats
            where s.Date >= since
            join p in db.Posts on s.PostId equals p.Id
            where ids.Contains(p.AuthorId)
            group s by new { p.AuthorId, p.Id, p.Title } into g
            select new { g.Key.AuthorId, g.Key.Title, Views = g.Sum(x => x.Views), Likes = g.Sum(x => x.Likes), Comments = g.Sum(x => x.Comments) }
        ).AsNoTracking().ToListAsync(ct)).Where(r => r.Views + r.Likes + r.Comments > 0).ToLookup(r => r.AuthorId);

        // One transaction: an interrupted run enqueues nothing, so the catch-up run can't send duplicates.
        // ponytail: a crash after commit but before the lease records LastRunAt can still re-send once; add a per-week unique key if that matters.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var u in users)
        {
            if (!rows.Contains(u.Id)) continue;
            var list = rows[u.Id].OrderByDescending(r => r.Views).Select(r => (r.Title, r.Views, r.Likes, r.Comments)).ToList();
            await queue.EnqueueAsync(db, Emails.Digest(u.Email, u.Name, list, $"{baseUrl}/analytics"), ct: ct);
        }
        await tx.CommitAsync(ct);
    }
}
