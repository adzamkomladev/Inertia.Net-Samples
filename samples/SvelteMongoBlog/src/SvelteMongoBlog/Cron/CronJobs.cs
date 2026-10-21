using NCrontab;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;
using System.Diagnostics;
using System.Text.Json;
using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Features.Alerts;
using SvelteMongoBlog.Features.Analytics;
using SvelteMongoBlog.Features.Bookings;
using SvelteMongoBlog.Features.Guard;
using SvelteMongoBlog.Features.Insurance;

namespace SvelteMongoBlog.Infrastructure;

/// <summary>TickerQ jobs retain database leases to fence duplicate runs across replicas.</summary>
public sealed class CronJobs(IServiceScopeFactory scopes, ILogger<CronJobs> log)
{
    static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(30);
    internal static readonly (string Name, string Cron, Func<IServiceProvider, CancellationToken, Task> Run)[] Schedule =
    [
        ("backup", "0 3 * * *", Backup),
        ("prune-jobs", "30 3 * * *", PruneJobs),
        ("orphan-assets", "0 * * * *", OrphanAssets),
        ("prune-unverified", "0 4 * * *", PruneUnverified),
        ("search-lot-reconcile", "*/5 * * * *", ReconcileSearch),
        ("lots-stat-rollup", "*/15 * * * *", Rollup),
        ("reservation-sweeper", "5 7 * * *", ReservationSweeper),
        ("no-show-check", "*/10 * * * *", NoShowCheck),
        ("overstay-check", "*/10 * * * *", OverstayCheck),
        ("policy-expiry", "0 4 * * *", PolicyExpiry),
    ];

    public static Dictionary<string, CrontabSchedule> Schedules(IConfiguration config) => Schedule.ToDictionary(s => s.Name, s =>
    {
        var text = config[$"Cron:{s.Name}"] is { Length: > 0 } c ? c : s.Cron;
        try { return CrontabSchedule.Parse(CronExpression.Parse(text).Value, new CrontabSchedule.ParseOptions { IncludingSeconds = true }); }
        catch (Exception e) when (e is ArgumentException or CrontabException) { throw new InvalidOperationException($"Invalid cron expression '{text}' for Cron:{s.Name}: {e.Message}", e); }
    });

    internal async Task<bool> Tick((string Name, string Cron, Func<IServiceProvider, CancellationToken, Task> Run) entry, CrontabSchedule expr, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>(); var now = DateTime.UtcNow;
        await db.CronLeases.UpdateOneAsync(x => x.Name == entry.Name, Builders<CronLease>.Update.SetOnInsert(x => x.Name, entry.Name).SetOnInsert(x => x.LastRunAt, now), new UpdateOptions { IsUpsert = true }, ct);
        var leaseDoc = await db.CronLeases.Find(x => x.Name == entry.Name).FirstAsync(ct);
        var due = leaseDoc.LastRunAt is null ? (DateTime?)null : expr.GetNextOccurrences(DateTime.SpecifyKind(leaseDoc.LastRunAt.Value, DateTimeKind.Utc), now.AddTicks(1)).LastOrDefault();
        if (due is not { } occurrence || occurrence == default) return false;
        var lease = now.Add(LeaseTtl);
        var claimed = await db.CronLeases.FindOneAndUpdateAsync(x => x.Name == entry.Name && (x.LockedUntil == null || x.LockedUntil < now) && (x.LastRunAt == null || x.LastRunAt < occurrence),
            Builders<CronLease>.Update.Set(x => x.LockedUntil, lease), new FindOneAndUpdateOptions<CronLease> { ReturnDocument = ReturnDocument.After }, ct);
        if (claimed is null) return true;
        var finished = false;
        try
        {
            log.LogInformation("Cron {Name} running (due {Due:u})", entry.Name, occurrence);
            await using var runScope = scopes.CreateAsyncScope(); await entry.Run(runScope.ServiceProvider, ct); finished = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e) { log.LogError(e, "Cron {Name} failed; waiting for next occurrence", entry.Name); finished = true; }
        finally
        {
            await using var releaseScope = scopes.CreateAsyncScope(); var db2 = releaseScope.ServiceProvider.GetRequiredService<AppDb>();
            var update = Builders<CronLease>.Update.Set(x => x.LockedUntil, null);
            if (finished) update = update.Set(x => x.LastRunAt, occurrence);
            await db2.CronLeases.UpdateOneAsync(x => x.Name == entry.Name && x.LockedUntil == lease, update, cancellationToken: CancellationToken.None);
        }
        return false;
    }

    static async Task Rollup(IServiceProvider sp, CancellationToken ct)
    {
        await LotAnalytics.RollupAsync(sp.GetRequiredService<AppDb>(), ct);
    }

    static async Task OrphanAssets(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>(); var queue = sp.GetRequiredService<JobQueue>();
        var pendingBefore = DateTime.UtcNow.AddHours(-1); var readyBefore = DateTime.UtcNow.AddHours(-24);
        for (var i = 0; i < 50; i++)
        {
            var candidates = await db.Assets.Find(x => (x.Status == AssetStatus.Pending && x.CreatedAt < pendingBefore) || (x.Status == AssetStatus.Ready && x.CreatedAt < readyBefore))
                .SortBy(x => x.Id).Limit(200).ToListAsync(ct);
            if (candidates.Count == 0) return;
            var ids = candidates.Select(x => x.Id).ToArray();
            // Avatars are the only uploads in this domain.
            var referencedAvatars = await db.Users.Find(x => x.AvatarAssetId != null && ids.Contains(x.AvatarAssetId.Value)).Project(x => x.AvatarAssetId!.Value).ToListAsync(ct);
            var remove = candidates.Where(x => !referencedAvatars.Contains(x.Id)).ToArray();
            if (remove.Length == 0) return;
            await db.TransactionAsync(async session =>
            {
                await db.Assets.DeleteManyAsync(session, Builders<Asset>.Filter.Where(x => remove.Select(a => a.Id).Contains(x.Id)), cancellationToken: ct);
                await queue.EnqueueAsync(db, new DeleteAssets(remove.Select(x => x.Key).ToArray()), session: session, ct: ct);
            }, ct);
            queue.Wake();
        }
    }

    static async Task PruneUnverified(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>(); var cutoff = DateTime.UtcNow.AddDays(-7);
        var users = await db.Users.Find(u => u.EmailVerifiedAt == null && u.CreatedAt < cutoff).Limit(500).ToListAsync(ct);
        foreach (var u in users)
        {
            if (await db.Reservations.Find(r => r.UserId == u.Id).AnyAsync(ct)) continue; // keep users with booking history
            await db.Users.DeleteOneAsync(x => x.Id == u.Id && x.EmailVerifiedAt == null, ct);
        }
    }

    static async Task PruneJobs(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>(); var done = DateTime.UtcNow.AddDays(-7); var failed = DateTime.UtcNow.AddDays(-30);
        await db.Jobs.DeleteManyAsync(x => (x.Status == JobStatus.Done && x.CreatedAt < done) || (x.Status == JobStatus.Failed && x.CreatedAt < failed), ct);
    }

    /// <summary>Daily sweep of tomorrow's materialized occurrences: reminder emails (pref-checked in the handler)
    /// plus alert rows keyed by the occurrence, deduplicated on the alert Body marker.</summary>
    static async Task ReservationSweeper(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>(); var jobs = sp.GetRequiredService<JobQueue>();
        var from = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(1), DateTimeKind.Utc);
        var to = from.AddDays(1);
        var upcoming = await db.Occurrences.Find(o => o.Status == OccurrenceStatus.Scheduled && o.StartAtUtc >= from && o.StartAtUtc < to).ToListAsync(ct);
        if (upcoming.Count == 0) return;
        var confirmed = await db.Reservations
            .Find(r => upcoming.Select(o => o.ReservationId).Contains(r.Id) && r.Status == ReservationStatus.Confirmed).ToListAsync(ct);
        var reservationUser = confirmed.ToDictionary(r => r.Id, r => r.UserId);
        foreach (var occurrence in upcoming)
        {
            if (!reservationUser.TryGetValue(occurrence.ReservationId, out var userId)) continue;
            // The reminder handler writes the alert row after emailing; its presence marks this occurrence as swept.
            if (await db.Alerts.Find(a => a.UserId == userId && a.Body == $"occurrence:{occurrence.Id}").AnyAsync(ct)) continue;
            await jobs.EnqueueAsync(db, new ReservationReminderEmail(occurrence.ReservationId, occurrence.Id), ct: ct);
        }
        jobs.Wake();
    }

    static async Task NoShowCheck(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        await GuardService.SweepNoShowsAsync(db, sp.GetRequiredService<JobQueue>(), sp.GetRequiredService<RealtimeEvents>(), ct);
    }

    static async Task OverstayCheck(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        await GuardService.FlagOverstaysAsync(db, sp.GetRequiredService<RealtimeEvents>(), ct);
    }

    static async Task PolicyExpiry(IServiceProvider sp, CancellationToken ct)
    {
        await InsuranceService.ExpireOverdueAsync(sp.GetRequiredService<AppDb>(), ct);
    }

    static async Task ReconcileSearch(IServiceProvider sp, CancellationToken ct)
    {
        if (!SearchIndexing.IsMeilisearchEnabled(sp.GetRequiredService<IConfiguration>()) || sp.GetService<MeilisearchTaskClient>() is null) return;
        await SearchIndexing.ReconcileAsync(sp.GetRequiredService<AppDb>(), sp.GetRequiredService<JobQueue>(), ct);
    }

    static async Task Backup(IServiceProvider sp, CancellationToken ct)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var storage = sp.GetRequiredService<Storage>();
        var now = DateTime.UtcNow;
        var environment = (config["APP_ENV"] ?? sp.GetRequiredService<IHostEnvironment>().EnvironmentName).ToLowerInvariant();
        var prefix = $"SvelteMongoBlog/{environment}/";
        var temporaryArchive = Path.Combine(Path.GetTempPath(), $"SvelteMongoBlog-{Guid.NewGuid():N}.archive.gz");
        var temporaryConfig = temporaryArchive + ".yaml";
        try
        {
            var mongoUrl = new MongoUrlBuilder(config["DATABASE_URL"] ?? config.GetConnectionString("Default"));
            if (mongoUrl.Username is not null) mongoUrl.AuthenticationSource ??= mongoUrl.DatabaseName ?? "admin";
            mongoUrl.DatabaseName = sp.GetRequiredService<AppDb>().Database.DatabaseNamespace.DatabaseName;

            var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var file = new FileStream(temporaryConfig, fileOptions))
            await using (var writer = new StreamWriter(file))
                await writer.WriteAsync(("uri: " + JsonSerializer.Serialize(mongoUrl.ToString())).AsMemory(), ct);

            var start = new ProcessStartInfo("mongodump") { UseShellExecute = false, RedirectStandardError = true };
            foreach (var arg in new[] { "--config", temporaryConfig, "--gzip", "--archive=" + temporaryArchive }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the MongoDB backup tool.");
            var errors = process.StandardError.ReadToEndAsync(ct);
            try { await process.WaitForExitAsync(ct); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            _ = await errors;
            if (process.ExitCode != 0) throw new InvalidOperationException($"mongodump exited with code {process.ExitCode}.");

            await using var archive = File.OpenRead(temporaryArchive);
            var key = $"{prefix}db/{now:yyyy-MM-dd-HHmm}.archive.gz";
            await storage.UploadBackupAsync(key, archive, ct);
        }
        finally
        {
            File.Delete(temporaryArchive);
            File.Delete(temporaryConfig);
        }

        var backups = await storage.ListBackupsAsync(prefix, ct);
        foreach (var old in backups.OrderByDescending(x => x, StringComparer.Ordinal).Skip(14))
            await storage.DeleteBackupAsync(old, ct);
    }
}
