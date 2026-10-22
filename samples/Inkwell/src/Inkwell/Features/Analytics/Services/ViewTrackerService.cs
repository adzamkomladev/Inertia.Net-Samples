using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Inkwell.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Features.Analytics;

/// <summary>
/// Records a deduplicated daily view (PostView, INSERT OR IGNORE) and bumps Post.ViewCount when the row is new.
/// Skips the post's author and bots. Only a per-day salted SHA-256 of the visitor is stored, never the IP or user
/// agent, so a visitor cannot be linked across days.
/// </summary>
public sealed partial class ViewTracker(AppDb db, IConfiguration config, TimeProvider time, ILogger<ViewTracker> logger, Inkwell.Jobs.JobQueue jobs)
{
    // ponytail: dev default salt; set Analytics:Salt in production.
    readonly string salt = config["Analytics:Salt"] ?? "inkwell-dev-salt";

    public async Task TrackAsync(ViewVisit visit, Guid postId, Guid authorId, CancellationToken ct)
    {
        try
        {
            if (visit.UserId == authorId || string.IsNullOrWhiteSpace(visit.UserAgent) || BotPattern().IsMatch(visit.UserAgent)) return;
            var day = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).ToString("yyyy-MM-dd");
            var identity = visit.UserId?.ToString() ?? $"{visit.IpAddress}|{visit.UserAgent}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{day}|{salt}|{identity}")));
            jobs.Enqueue(db, new PostViewed(postId, hash, day, visit.UserId));
            await db.SaveChangesAsync(ct);
            jobs.Wake();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "View tracking failed for post {PostId}", postId);
        }
    }

    public async Task RecordAsync(PostViewed job, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Atomic insert-if-absent keeps retries from counting the same visitor twice.
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT OR IGNORE INTO PostViews (PostId, VisitorHash, Day)
            SELECT {job.PostId}, {job.VisitorHash}, {job.Day}
            WHERE EXISTS (SELECT 1 FROM Posts WHERE Id = {job.PostId})
            """, ct);
        if (inserted == 0) return;
        await db.Posts.Where(p => p.Id == job.PostId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ViewCount, p => p.ViewCount + 1), ct);
        jobs.Enqueue(db, new Inkwell.Jobs.Handlers.PostChanged(job.PostId, job.ViewerId, null, false));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        jobs.Wake();
    }

    [GeneratedRegex("bot|crawl|spider|slurp|preview|monitor|curl|wget|python-requests", RegexOptions.IgnoreCase)]
    private static partial Regex BotPattern();
}
