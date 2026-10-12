using NCrontab;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;
using System.Diagnostics;
using System.Text.Json;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;

namespace SvelteMongoReplicasBlog.Infrastructure;

/// <summary>TickerQ jobs retain database leases to fence duplicate runs across replicas.</summary>
public sealed class CronJobs(IServiceScopeFactory scopes, ILogger<CronJobs> log)
{
    static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(30);
    internal static readonly (string Name, string Cron, Func<IServiceProvider, CancellationToken, Task> Run)[] Schedule =
    [
        ("backup", "0 3 * * *", Backup),
        ("analytics-rollup", "*/15 * * * *", Rollup),
        ("prune-views", "0 3 * * *", PruneViews),
        ("orphan-assets", "0 * * * *", OrphanAssets),
        ("prune-unverified", "0 4 * * *", PruneUnverified),
        ("prune-jobs", "30 3 * * *", PruneJobs),
        ("refresh-interest-profiles", "*/5 * * * *", RefreshProfiles),
        ("search-index-reconcile", "*/5 * * * *", ReconcileSearch),
        ("daily-recommendation-digest", "*/5 * * * *", DailyRecommendationDigest),
        ("weekly-digest", "0 8 * * 1", WeeklyDigest),
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
        var db = sp.GetRequiredService<AppDb>();
        foreach (var day in new[] { DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), DateOnly.FromDateTime(DateTime.UtcNow) })
        {
            var from = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); var to = from.AddDays(1);
            var views = await db.PostViews.Aggregate().Match(v => v.At >= from && v.At < to)
                .Group(v => v.PostId, g => new { PostId = g.Key, Views = g.Count(), Unique = g.Select(x => x.ViewerHash).Distinct().Count() }).ToListAsync(ct);
            var likes = await db.Likes.Aggregate().Match(l => l.CreatedAt >= from && l.CreatedAt < to).Group(l => l.PostId, g => new { PostId = g.Key, Count = g.Count() }).ToListAsync(ct);
            var comments = await db.Comments.Aggregate().Match(c => c.CreatedAt >= from && c.CreatedAt < to).Group(c => c.PostId, g => new { PostId = g.Key, Count = g.Count() }).ToListAsync(ct);
            var postIds = views.Select(x => x.PostId).Concat(likes.Select(x => x.PostId)).Concat(comments.Select(x => x.PostId)).Distinct().ToArray();
            if (postIds.Length == 0) { await db.PostDailyStats.DeleteManyAsync(x => x.Date == day, ct); continue; }
            var viewMap = views.ToDictionary(x => x.PostId); var likeMap = likes.ToDictionary(x => x.PostId, x => x.Count); var commentMap = comments.ToDictionary(x => x.PostId, x => x.Count);
            var rows = postIds.Select(id => new PostDailyStat { PostId = id, Date = day, Views = viewMap.GetValueOrDefault(id)?.Views ?? 0,
                UniqueViewers = viewMap.GetValueOrDefault(id)?.Unique ?? 0, Likes = likeMap.GetValueOrDefault(id), Comments = commentMap.GetValueOrDefault(id) }).ToList();
            await db.TransactionAsync(async session =>
            {
                await db.PostDailyStats.DeleteManyAsync(session, Builders<PostDailyStat>.Filter.Where(x => x.Date == day), cancellationToken: ct);
                await db.PostDailyStats.InsertManyAsync(session, rows, cancellationToken: ct);
            }, ct);
        }
    }

    static async Task PruneViews(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>(); var cutoff = DateTime.UtcNow.AddDays(-90);
        while (true)
        {
            var ids = await db.PostViews.Find(x => x.At < cutoff).SortBy(x => x.At).Limit(5000).Project(x => x.Id).ToListAsync(ct);
            if (ids.Count == 0) return;
            await db.PostViews.DeleteManyAsync(x => ids.Contains(x.Id), ct);
            await Task.Delay(50, ct);
        }
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
            var referencedPosts = await db.Posts.Find(x => x.CoverAssetId != null && ids.Contains(x.CoverAssetId.Value)).Project(x => x.CoverAssetId!.Value).ToListAsync(ct);
            var referencedAvatars = await db.Users.Find(x => x.AvatarAssetId != null && ids.Contains(x.AvatarAssetId.Value)).Project(x => x.AvatarAssetId!.Value).ToListAsync(ct);
            var referenced = referencedPosts.Concat(referencedAvatars).ToHashSet();
            var remove = candidates.Where(x => x.Status == AssetStatus.Pending || !referenced.Contains(x.Id)).ToArray();
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
            if (await db.Posts.Find(p => p.AuthorId == u.Id).AnyAsync(ct) || await db.Comments.Find(c => c.UserId == u.Id).AnyAsync(ct)) continue;
            await db.Users.DeleteOneAsync(x => x.Id == u.Id && x.EmailVerifiedAt == null, ct);
        }
    }

    static async Task PruneJobs(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>(); var done = DateTime.UtcNow.AddDays(-7); var failed = DateTime.UtcNow.AddDays(-30);
        await db.Jobs.DeleteManyAsync(x => (x.Status == JobStatus.Done && x.CreatedAt < done) || (x.Status == JobStatus.Failed && x.CreatedAt < failed), ct);
    }

    static async Task Backup(IServiceProvider sp, CancellationToken ct)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var storage = sp.GetRequiredService<Storage>();
        var now = DateTime.UtcNow;
        var environment = (config["APP_ENV"] ?? sp.GetRequiredService<IHostEnvironment>().EnvironmentName).ToLowerInvariant();
        var prefix = $"SvelteMongoReplicasBlog/{environment}/";
        var temporaryArchive = Path.Combine(Path.GetTempPath(), $"SvelteMongoReplicasBlog-{Guid.NewGuid():N}.archive.gz");
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

    static async Task RefreshProfiles(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>(); var jobs = sp.GetRequiredService<JobQueue>();
        await sp.GetRequiredService<SvelteMongoReplicasBlog.Features.Discovery.DiscoveryService>().RefreshProfilesAsync(db, jobs, ct);
    }

    static async Task ReconcileSearch(IServiceProvider sp, CancellationToken ct)
    {
        if (!SearchIndexing.IsMeilisearchEnabled(sp.GetRequiredService<IConfiguration>()) || sp.GetService<MeilisearchTaskClient>() is null) return;
        await SearchIndexing.ReconcileAsync(sp.GetRequiredService<AppDb>(), sp.GetRequiredService<JobQueue>(), ct);
    }

    static async Task DailyRecommendationDigest(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>(); var jobs = sp.GetRequiredService<JobQueue>();
        await sp.GetRequiredService<DigestService>().EnqueueDueAsync(db, jobs, ct);
    }

    static async Task WeeklyDigest(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>(); var queue = sp.GetRequiredService<JobQueue>();
        var baseUrl = sp.GetRequiredService<IConfiguration>().AppBaseUrl() ?? ""; var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-6);
        var users = await db.Users.Find(u => u.EmailVerifiedAt != null && u.Preferences.WeeklyDigest).ToListAsync(ct);
        var userIds = users.Select(u => u.Id).ToArray(); if (userIds.Length == 0) return;
        var owned = await db.Posts.Find(p => userIds.Contains(p.AuthorId)).Project(p => new { p.Id, p.AuthorId, p.Title }).ToListAsync(ct);
        var postIds = owned.Select(p => p.Id).ToArray();
        var stats = postIds.Length == 0 ? [] : await db.PostDailyStats.Find(s => postIds.Contains(s.PostId) && s.Date >= since).ToListAsync(ct);
        var postMap = owned.ToDictionary(x => x.Id); var grouped = stats.GroupBy(s => postMap[s.PostId].AuthorId)
            .ToDictionary(g => g.Key, g => g.GroupBy(s => s.PostId).Select(p => (postMap[p.Key].Title, Views: p.Sum(x => x.Views), Likes: p.Sum(x => x.Likes), Comments: p.Sum(x => x.Comments))).Where(x => x.Views + x.Likes + x.Comments > 0).OrderByDescending(x => x.Views).ToList());
        await db.TransactionAsync(async session =>
        {
            foreach (var user in users)
                if (grouped.TryGetValue(user.Id, out var rows) && rows.Count > 0)
                    await queue.EnqueueAsync(db, Emails.Digest(user.Email, user.Name, rows, $"{baseUrl}/analytics"), session: session, ct: ct);
        }, ct);
        queue.Wake();
    }
}
