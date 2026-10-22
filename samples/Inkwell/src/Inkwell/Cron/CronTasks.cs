using TickerQ.Utilities;
using Inkwell.Data;
using Inkwell.Features.Discovery;
using Inkwell.Infrastructure;
using Inkwell.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Cron;

public static class CronTasks
{
    public static readonly CronTask[] All =
    [
        new("orphan-uploads", CronExpression.Parse("0 * * * *"), Run(OrphanUploadsAsync)),
        new("prune", CronExpression.Parse("30 3 * * *"), Run(PruneAsync)),
        new("unverified-users", CronExpression.Parse("0 4 * * *"), Run(UnverifiedUsersAsync)),
        new("recommendation-digest", CronExpression.Parse("*/5 * * * *"), Run(RecommendationDigestAsync)),
        new("interest-profiles", CronExpression.Parse("*/5 * * * *"), Run(InterestProfilesAsync)),
        new("weekly-digest", CronExpression.Parse("0 8 * * 1"), Run(WeeklyDigestAsync)),
    ];

    internal static async Task RecommendationDigestAsync(IServiceProvider sp, AppDb db, DateTime now, CancellationToken ct) =>
        await sp.GetRequiredService<DigestService>().EnqueueDueAsync(ct);

    internal static async Task InterestProfilesAsync(IServiceProvider sp, AppDb db, DateTime now, CancellationToken ct)
    {
        var jobs = sp.GetRequiredService<JobQueue>();
        await sp.GetRequiredService<DiscoveryService>().RefreshProfilesAsync(ct: ct);
        await db.SaveChangesAsync(ct);
        jobs.Wake();
    }

    private static Func<IServiceProvider, CancellationToken, Task> Run(Func<IServiceProvider, AppDb, DateTime, CancellationToken, Task> task) =>
        async (sp, ct) => await task(sp, sp.GetRequiredService<AppDb>(), sp.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime, ct);

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
    internal static async Task UnverifiedUsersAsync(IServiceProvider sp, AppDb db, DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-7);
        await db.Users
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
