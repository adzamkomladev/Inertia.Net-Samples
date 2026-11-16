using System.Globalization;
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
        new("orphan-uploads", "15 */6 * * *", Run(OrphanUploadsAsync)), // 24 h grace, so four sweeps a day is plenty
        new("prune", "30 3 * * *", Run(PruneAsync)),
        new("unverified-users", "0 4 * * *", Run(UnverifiedUsersAsync)),
        new("recommendation-digest", "*/5 * * * *", Run(RecommendationDigestAsync)),
        new("interest-profiles", "*/5 * * * *", Run(InterestProfilesAsync)),
        new("weekly-digest", "0 8 * * 1", Run(WeeklyDigestAsync)),
    ];

    internal static async Task RecommendationDigestAsync(IServiceProvider sp, AppDb db, DateTime now, CancellationToken ct) =>
        await sp.GetRequiredService<DigestService>().EnqueueDueAsync(ct);

    internal static async Task InterestProfilesAsync(IServiceProvider sp, AppDb db, DateTime now, CancellationToken ct) =>
        await sp.GetRequiredService<DiscoveryService>().RefreshDirtyProfilesAsync(ct: ct);

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
        await db.PostViews.Where(v => v.Day < dayCutoff).ExecuteDeleteAsync(ct);
        // Expired feed snapshots were never read again but stayed on disk; finished Runwick jobs follow Runwick's retention.
        await db.FeedSnapshots.Where(s => s.ExpiresAt < now).ExecuteDeleteAsync(ct);
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
        var userIds = await db.Users
            .Where(u => u.EmailConfirmed && u.Preferences.WeeklyDigest)
            .Select(u => u.Id)
            .ToListAsync(ct);
        // Unique per user and week (RunwickSetup), so a retried cron run cannot send a second digest.
        await sp.GetRequiredService<IJobOutbox>().DispatchManyAsync(
            userIds.Select(id => (Runwick.IQueueJob)new WeeklyDigestEmail(id, ISOWeek.GetYear(now) * 100 + ISOWeek.GetWeekOfYear(now))).ToList(), ct);
    }
}
