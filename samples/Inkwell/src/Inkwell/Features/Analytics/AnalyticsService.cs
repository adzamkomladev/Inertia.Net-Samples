using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Inkwell.Data;
using Inkwell.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Features.Analytics;

public sealed record DayRow(string Date, int Views, int Likes, int Comments);

public sealed record SummaryDto(int Views, int Likes, int Comments, int Posts, int PrevViews, int PrevLikes, int PrevComments);

public sealed record TopPostDto(Guid Id, string Title, int Views, int Likes, int Comments, int LifetimeViews);

public sealed record PostHeader(Guid Id, string Title, Guid AuthorId, DateTime CreatedAt, int ViewCount, int LikeCount, int CommentCount);

/// <summary>The HTTP-independent facts a view beacon contributes to analytics.</summary>
public sealed record ViewVisit(Guid? UserId, string? IpAddress, string UserAgent);

/// <summary>
/// Dashboard queries. Every method opens its own context from the factory, so Inertia can run them as
/// parallel props. A null post id means all of the user's posts.
/// </summary>
public sealed class AnalyticsService(IDbContextFactory<AppDb> dbs, TimeProvider time)
{
    public static int ClampRange(int? range) => range is 7 or 90 ? range.Value : 30;

    public (DateOnly From, DateOnly To) Window(int days)
    {
        var to = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        return (to.AddDays(-(days - 1)), to);
    }

    /// <summary>Zero-filled views, likes and comments for every day in [from, to], from one query.</summary>
    public async Task<DayRow[]> DailyAsync(Guid userId, Guid? postId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var db = await dbs.CreateDbContextAsync(ct);
        var (f, t, end) = (Day(from), Day(to), Day(to.AddDays(1)));
        // DateOnly is stored as yyyy-MM-dd text and DateTime as "yyyy-MM-dd HH:mm:ss…", so text ranges and substr work,
        // and the ranges on CreatedAt can use the (PostId, CreatedAt) indexes.
        var counts = await db.Database.SqlQuery<DayCount>($"""
            SELECT 'v' AS Kind, pv.Day AS Day, COUNT(*) AS N FROM PostViews pv JOIN Posts p ON p.Id = pv.PostId
            WHERE p.AuthorId = {userId} AND ({postId} IS NULL OR p.Id = {postId}) AND pv.Day >= {f} AND pv.Day <= {t}
            GROUP BY pv.Day
            UNION ALL
            SELECT 'l', substr(l.CreatedAt, 1, 10), COUNT(*) FROM PostLikes l JOIN Posts p ON p.Id = l.PostId
            WHERE p.AuthorId = {userId} AND ({postId} IS NULL OR p.Id = {postId}) AND l.CreatedAt >= {f} AND l.CreatedAt < {end}
            GROUP BY substr(l.CreatedAt, 1, 10)
            UNION ALL
            SELECT 'c', substr(c.CreatedAt, 1, 10), COUNT(*) FROM Comments c JOIN Posts p ON p.Id = c.PostId
            WHERE p.AuthorId = {userId} AND ({postId} IS NULL OR p.Id = {postId}) AND c.CreatedAt >= {f} AND c.CreatedAt < {end}
            GROUP BY substr(c.CreatedAt, 1, 10)
            """).ToListAsync(ct);
        var byKey = counts.ToDictionary(c => (c.Kind, c.Day), c => c.N);
        return Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(i => Day(from.AddDays(i)))
            .Select(day => new DayRow(day, byKey.GetValueOrDefault(("v", day)), byKey.GetValueOrDefault(("l", day)), byKey.GetValueOrDefault(("c", day))))
            .ToArray();
    }

    /// <summary>rows = the previous period followed by the current one, which starts at <paramref name="currentFrom"/>.</summary>
    public static SummaryDto Summarize(DayRow[] rows, DateOnly currentFrom, int posts)
    {
        var from = Day(currentFrom);
        var current = rows.Where(r => string.CompareOrdinal(r.Date, from) >= 0).ToArray();
        var previous = rows.Where(r => string.CompareOrdinal(r.Date, from) < 0).ToArray();
        return new SummaryDto(current.Sum(r => r.Views), current.Sum(r => r.Likes), current.Sum(r => r.Comments), posts,
            previous.Sum(r => r.Views), previous.Sum(r => r.Likes), previous.Sum(r => r.Comments));
    }

    public static DayRow[] CurrentRows(DayRow[] rows, DateOnly currentFrom) =>
        [.. rows.Where(r => string.CompareOrdinal(r.Date, Day(currentFrom)) >= 0)];

    public async Task<int> PostCountAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await dbs.CreateDbContextAsync(ct);
        return await db.Posts.CountAsync(p => p.AuthorId == userId, ct);
    }

    public async Task<PostHeader?> PostHeaderAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbs.CreateDbContextAsync(ct);
        return await db.Posts.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new PostHeader(p.Id, p.Title, p.AuthorId, p.CreatedAt, p.ViewCount, p.LikeCount, p.CommentCount))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<TopPostDto[]> TopPostsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var db = await dbs.CreateDbContextAsync(ct);
        var (f, t, end) = (Day(from), Day(to), Day(to.AddDays(1)));
        return await db.Database.SqlQuery<TopPostDto>($"""
            SELECT p.Id AS Id, p.Title AS Title, p.ViewCount AS LifetimeViews,
              (SELECT COUNT(*) FROM PostViews v WHERE v.PostId = p.Id AND v.Day >= {f} AND v.Day <= {t}) AS Views,
              (SELECT COUNT(*) FROM PostLikes l WHERE l.PostId = p.Id AND l.CreatedAt >= {f} AND l.CreatedAt < {end}) AS Likes,
              (SELECT COUNT(*) FROM Comments c WHERE c.PostId = p.Id AND c.CreatedAt >= {f} AND c.CreatedAt < {end}) AS Comments
            FROM Posts p WHERE p.AuthorId = {userId}
            ORDER BY Views DESC, p.CreatedAt DESC LIMIT 10
            """).ToArrayAsync(ct);
    }

    static string Day(DateOnly d) => d.ToString("yyyy-MM-dd");

    sealed class DayCount
    {
        public string Kind { get; set; } = "";
        public string Day { get; set; } = "";
        public int N { get; set; }
    }
}

/// <summary>
/// Records a deduplicated daily view (PostView, INSERT OR IGNORE) and bumps Post.ViewCount when the row is new.
/// Skips the post's author and bots. Only a per-day salted SHA-256 of the visitor is stored, never the IP or user
/// agent, so a visitor cannot be linked across days.
/// </summary>
public sealed partial class ViewTracker(AppDb db, IConfiguration config, TimeProvider time, ILogger<ViewTracker> logger, RealtimeEvents realtime)
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
            var inserted = await db.Database.ExecuteSqlAsync(
                $"INSERT OR IGNORE INTO PostViews (PostId, VisitorHash, Day) VALUES ({postId}, {hash}, {day})", ct);
            if (inserted == 0) return;
            await db.Posts.Where(p => p.Id == postId).ExecuteUpdateAsync(s => s.SetProperty(p => p.ViewCount, p => p.ViewCount + 1), ct);
            await realtime.ChangedAsync(postId, visit.UserId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "View tracking failed for post {PostId}", postId);
        }
    }

    [GeneratedRegex("bot|crawl|spider|slurp|preview|monitor|curl|wget|python-requests", RegexOptions.IgnoreCase)]
    private static partial Regex BotPattern();
}
