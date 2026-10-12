using System.IO.Compression;
using TickerQ.Utilities;
using ReactSqliteBlog.Data;
using ReactSqliteBlog.Infrastructure;
using ReactSqliteBlog.Jobs;
using Microsoft.EntityFrameworkCore;

namespace ReactSqliteBlog.Cron;

public static class CronTasks
{
    public static readonly CronTask[] All =
    [
        new("backup", CronExpression.Parse("0 3 * * *"), Run(BackupAsync)),
        new("orphan-uploads", CronExpression.Parse("0 * * * *"), Run(OrphanUploadsAsync)),
        new("prune", CronExpression.Parse("30 3 * * *"), Run(PruneAsync)),
        new("unverified-users", CronExpression.Parse("0 4 * * *"), Run(UnverifiedUsersAsync)),
        new("recommendation-digest", CronExpression.Parse("*/5 * * * *"), Run(RecommendationDigestAsync)),
        new("search-reconcile", CronExpression.Parse("*/5 * * * *"), Run(SearchReconcileAsync)),
        new("interest-profiles", CronExpression.Parse("*/5 * * * *"), Run(InterestProfilesAsync)),
        new("weekly-digest", CronExpression.Parse("0 8 * * 1"), Run(WeeklyDigestAsync)),
    ];

    internal static async Task RecommendationDigestAsync(IServiceProvider sp, AppDb db, DateTime now, CancellationToken ct)
    {
        var jobs = sp.GetRequiredService<JobQueue>();
        await sp.GetRequiredService<DigestService>().EnqueueDueAsync(db, jobs, ct);
        jobs.Wake();
    }

    internal static async Task SearchReconcileAsync(IServiceProvider sp, AppDb db, DateTime now, CancellationToken ct)
    {
        if (sp.GetRequiredService<IConfiguration>()["SEARCH_DRIVER"] != "meilisearch") return;
        var jobs = sp.GetRequiredService<JobQueue>();
        await SearchIndexing.ReconcileAsync(db, jobs, ct);
        await db.SaveChangesAsync(ct);
        jobs.Wake();
    }

    internal static async Task InterestProfilesAsync(IServiceProvider sp, AppDb db, DateTime now, CancellationToken ct)
    {
        var jobs = sp.GetRequiredService<JobQueue>();
        await sp.GetRequiredService<ReactSqliteBlog.Features.Discovery.DiscoveryService>().RefreshProfilesAsync(db, jobs, ct);
        await db.SaveChangesAsync(ct);
        jobs.Wake();
    }

    private const int BackupsToKeep = 14;

    private static Func<IServiceProvider, CancellationToken, Task> Run(Func<IServiceProvider, AppDb, DateTime, CancellationToken, Task> task) =>
        (sp, ct) => task(sp, sp.GetRequiredService<AppDb>(), sp.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime, ct);

    /// <summary>VACUUM INTO a temp file, gzip it, upload, then keep only the newest 14 backups.</summary>
    internal static async Task BackupAsync(IServiceProvider sp, AppDb db, DateTime now, CancellationToken ct)
    {
        var backupPrefix = "backups/ReactSqliteBlog/" + (sp.GetService<IHostEnvironment>()?.EnvironmentName.ToLowerInvariant() ?? "production") + "/";
        var storage = sp.GetRequiredService<Storage>();
        var dbPath = Path.Combine(Path.GetTempPath(), $"inertiablog-{Guid.NewGuid():N}.db");
        var gzPath = dbPath + ".gz";
        try
        {
            var vacuum = "VACUUM INTO '" + dbPath.Replace("'", "''") + "'"; // VACUUM can't take a bound parameter
            await db.Database.ExecuteSqlRawAsync(vacuum, ct);
            await using (var input = File.OpenRead(dbPath))
            await using (var output = File.Create(gzPath))
            await using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            {
                await input.CopyToAsync(gzip, ct);
            }
            await using (var upload = File.OpenRead(gzPath))
            {
                await storage.UploadBackupAsync($"{backupPrefix}{now:yyyy-MM-dd-HHmm}.db.gz", upload, ct);
            }
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(gzPath);
        }

        // Keys are timestamped, so ordinal order is chronological.
        var keys = await storage.ListBackupsAsync(backupPrefix, ct);
        foreach (var old in keys.OrderByDescending(k => k, StringComparer.Ordinal).Skip(BackupsToKeep))
            await storage.DeleteBackupAsync(old, ct);
    }

    /// <summary>Deletes uploads never attached to a post within 24 h (at most 500 per run).</summary>
    internal static async Task OrphanUploadsAsync(IServiceProvider sp, AppDb db, DateTime now, CancellationToken ct)
    {
        var storage = sp.GetRequiredService<Storage>();
        var logger = sp.GetRequiredService<ILogger<CronTask>>();
        var cutoff = now.AddHours(-24);
        var orphans = await db.Uploads.AsNoTracking()
            .Where(u => u.PostId == null && !u.IsAvatar && u.CreatedAt < cutoff)
            .OrderBy(u => u.CreatedAt)
            .Select(u => new { u.Id, u.Key })
            .Take(500)
            .ToListAsync(ct);

        foreach (var upload in orphans)
        {
            // Claim by deleting the row first, in one statement that re-checks every live reference: a post body or
            // cover (an image reused in several posts keeps only one PostId) or an avatar. A request attaching the
            // upload concurrently either wins (row kept) or finds it gone; the object is only deleted after the claim.
            var claimed = await db.Uploads
                .Where(u => u.Id == upload.Id && u.PostId == null && !u.IsAvatar
                    && !db.Posts.IgnoreQueryFilters().Any(p => p.CoverUrl == u.Url || p.Body.Contains(u.Url))
                    && !db.Users.Any(x => x.AvatarUrl == u.Url))
                .ExecuteDeleteAsync(ct);
            if (claimed == 0) continue;

            try { await storage.DeleteMediaAsync(upload.Key, CancellationToken.None); }
            catch (Exception ex)
            {
                // ponytail: the object leaks in the bucket; a bucket-vs-table sweep can reclaim these if it matters.
                logger.LogWarning(ex, "Could not delete orphan upload {Key}", upload.Key);
            }
        }
    }

    internal static async Task PruneAsync(IServiceProvider sp, AppDb db, DateTime now, CancellationToken ct)
    {
        var dayCutoff = DateOnly.FromDateTime(now).AddDays(-365);
        var doneCutoff = now.AddDays(-7);
        var failedCutoff = now.AddDays(-30);
        await db.PostViews.Where(v => v.Day < dayCutoff).ExecuteDeleteAsync(ct);
        await db.Jobs.Where(j => j.Status == JobStatus.Done && j.UpdatedAt < doneCutoff).ExecuteDeleteAsync(ct);
        await db.Jobs.Where(j => j.Status == JobStatus.Failed && j.UpdatedAt < failedCutoff).ExecuteDeleteAsync(ct);
    }

    /// <summary>Deletes accounts never confirmed within 7 days that have written nothing. FKs cascade.</summary>
    internal static Task UnverifiedUsersAsync(IServiceProvider sp, AppDb db, DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-7);
        return db.Users
            .Where(u => !u.EmailConfirmed && u.CreatedAt < cutoff
                && !db.Posts.IgnoreQueryFilters().Any(p => p.AuthorId == u.Id)
                && !db.Comments.IgnoreQueryFilters().Any(c => c.AuthorId == u.Id))
            .ExecuteDeleteAsync(ct);
    }

    internal static async Task WeeklyDigestAsync(IServiceProvider sp, AppDb db, DateTime now, CancellationToken ct)
    {
        var queue = sp.GetRequiredService<JobQueue>();
        var userIds = await db.Users
            .Where(u => u.EmailConfirmed && u.Preferences.WeeklyDigest)
            .Select(u => u.Id)
            .ToListAsync(ct);
        foreach (var id in userIds) queue.Enqueue(db, new WeeklyDigestEmail(id));
        await db.SaveChangesAsync(ct);
        queue.Wake();
    }
}
