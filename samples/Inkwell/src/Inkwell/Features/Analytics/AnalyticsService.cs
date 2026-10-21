using System.Security.Cryptography;
using System.Text;
using Inkwell.Data;
using Inkwell.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Features.Analytics;

/// <summary>The HTTP-independent facts a view beacon contributes to analytics.</summary>
public sealed record ViewVisit(Guid? UserId, string? IpAddress, string UserAgent);


/// <summary>Shared by the overview and per-post pages. postId == null means all of the user's posts.</summary>
public static partial class AnalyticsQueries
{
    /// <summary>Zero-filled per-day rows for every day in [from, to].</summary>
    public static async Task<DayRow[]> SeriesAsync(
        AppDb db,
        Guid userId,
        Guid? postId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct
    )
    {
        var (f, t) = (D(from), D(to));
        // EF stores DateOnly as TEXT yyyy-MM-dd and DateTime as TEXT "yyyy-MM-dd HH:mm:ss...", so substr/string compare work.
        var views = await db
            .Database.SqlQuery<DayCount>(
                $"""
                SELECT pv.Day AS Day, COUNT(*) AS N FROM PostViews pv JOIN Posts p ON p.Id = pv.PostId
                WHERE p.AuthorId = {userId} AND ({postId} IS NULL OR p.Id = {postId}) AND pv.Day >= {f} AND pv.Day <= {t}
                GROUP BY pv.Day
                """
            )
            .ToDictionaryAsync(r => r.Day, r => r.N, ct);
        var likes = await db
            .Database.SqlQuery<DayCount>(
                $"""
                SELECT substr(l.CreatedAt, 1, 10) AS Day, COUNT(*) AS N FROM PostLikes l JOIN Posts p ON p.Id = l.PostId
                WHERE p.AuthorId = {userId} AND ({postId} IS NULL OR p.Id = {postId})
                  AND substr(l.CreatedAt, 1, 10) >= {f} AND substr(l.CreatedAt, 1, 10) <= {t}
                GROUP BY Day
                """
            )
            .ToDictionaryAsync(r => r.Day, r => r.N, ct);
        var comments = await db
            .Database.SqlQuery<DayCount>(
                $"""
                SELECT substr(c.CreatedAt, 1, 10) AS Day, COUNT(*) AS N FROM Comments c JOIN Posts p ON p.Id = c.PostId
                WHERE p.AuthorId = {userId} AND ({postId} IS NULL OR p.Id = {postId})
                  AND substr(c.CreatedAt, 1, 10) >= {f} AND substr(c.CreatedAt, 1, 10) <= {t}
                GROUP BY Day
                """
            )
            .ToDictionaryAsync(r => r.Day, r => r.N, ct);
        var rows = new List<DayRow>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var k = D(d);
            rows.Add(
                new DayRow(
                    k,
                    views.GetValueOrDefault(k),
                    likes.GetValueOrDefault(k),
                    comments.GetValueOrDefault(k)
                )
            );
        }

        return [.. rows];
    }

    public static async Task<int> PostCountAsync(AppDb db, Guid userId, CancellationToken ct) =>
        await db.Posts.CountAsync(p => p.AuthorId == userId, ct);

    /// <summary>Current-window rows: filters the previous+current series the shared loader already fetched.</summary>
    public static DayRow[] CurrentRows(DayRow[] rows, string fromText) =>
        [.. rows.Where(r => r.Date.CompareTo(fromText) >= 0)];

    internal static async Task<PostHeader?> PostHeaderAsync(AppDb db, Guid id, CancellationToken ct) =>
        await db
            .Posts.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PostHeader(
                p.Id,
                p.Title,
                p.AuthorId,
                p.CreatedAt,
                p.ViewCount,
                p.LikeCount,
                p.CommentCount
            ))
            .FirstOrDefaultAsync(ct);

    public static async Task<TopPostDto[]> TopPostsAsync(
        AppDb db,
        Guid userId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct
    )
    {
        var (f, t) = (D(from), D(to));
        var rows = await db
            .Database.SqlQuery<TopPostRow>(
                $"""
                SELECT p.Id AS Id, p.Title AS Title, p.ViewCount AS LifetimeViews,
                  (SELECT COUNT(*) FROM PostViews v WHERE v.PostId = p.Id AND v.Day >= {f} AND v.Day <= {t}) AS Views,
                  (SELECT COUNT(*) FROM PostLikes l WHERE l.PostId = p.Id
                     AND substr(l.CreatedAt, 1, 10) >= {f} AND substr(l.CreatedAt, 1, 10) <= {t}) AS Likes,
                  (SELECT COUNT(*) FROM Comments c WHERE c.PostId = p.Id
                     AND substr(c.CreatedAt, 1, 10) >= {f} AND substr(c.CreatedAt, 1, 10) <= {t}) AS Comments
                FROM Posts p WHERE p.AuthorId = {userId}
                ORDER BY Views DESC, p.CreatedAt DESC LIMIT 10
                """
            )
            .ToListAsync(ct);
        return
        [
            .. rows.Select(r => new TopPostDto(
                r.Id,
                r.Title,
                r.Views,
                r.Likes,
                r.Comments,
                r.LifetimeViews
            )),
        ];
    }
}

/// <summary>
/// Records a deduplicated daily view (PostView, INSERT OR IGNORE) and bumps Post.ViewCount when the row is new.
/// Skips the post's author, bots and prefetches. Called by GET /posts/{id}.
/// Privacy: only a per-day salted SHA-256 of the visitor identity is stored, never the IP or user agent,
/// and hashes differ every day so a visitor cannot be linked across days.
/// </summary>
public sealed partial class ViewTracker(
    AppDb db,
    IConfiguration config,
    ILogger<ViewTracker> logger,
    RealtimeEvents realtime
)
{
    // ponytail: dev default salt; set Analytics:Salt in production.
    private readonly string _secret = config["Analytics:Salt"] ?? "inertiablog-dev-salt";

    public async Task TrackAsync(
        ViewVisit visit,
        Guid postId,
        Guid authorId,
        CancellationToken ct
    )
    {
        try
        {
            var userId = visit.UserId;
            if (userId == authorId)
                return;
            var ua = visit.UserAgent;
            if (string.IsNullOrWhiteSpace(ua) || BotRegex().IsMatch(ua))
                return;
            var day = DateOnly.FromDateTime(DateTime.UtcNow);
            var dayText = day.ToString("yyyy-MM-dd");
            var identity = userId?.ToString() ?? $"{visit.IpAddress}|{ua}";
            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes($"{dayText}|{_secret}|{identity}"))
            );
            var inserted = await db.Database.ExecuteSqlAsync(
                $"INSERT OR IGNORE INTO PostViews (PostId, VisitorHash, Day) VALUES ({postId}, {hash}, {dayText})",
                ct
            );
            if (inserted == 1)
            {
                await db
                    .Posts.Where(p => p.Id == postId)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(p => p.ViewCount, p => p.ViewCount + 1),
                        ct
                    );
                await realtime.ChangedAsync(postId, userId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "View tracking failed for post {PostId}", postId);
        }
    }
}
