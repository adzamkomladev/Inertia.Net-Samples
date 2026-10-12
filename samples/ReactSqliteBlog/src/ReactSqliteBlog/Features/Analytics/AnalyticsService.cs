using System.Security.Cryptography;
using System.Text;
using ReactSqliteBlog.Data;
using ReactSqliteBlog.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static ReactSqliteBlog.Features.Analytics.AnalyticsHelpers;

namespace ReactSqliteBlog.Features.Analytics;

public static partial class AnalyticsService
{
    internal static IResult Overview(HttpContext ctx, AppDb db, int? range)
    {
        var userId = ctx.User.UserId()!.Value;
        var days = ClampRange(range);
        var (from, to) = Window(days);
        var fromText = from.ToString("yyyy-MM-dd");
        // Loaders run sequentially, so the shared scoped DbContext is safe. The previous+current
        // series is loaded once and reused by "summary" and "series".
        Task<DayRow[]>? both = null;
        Task<DayRow[]> Both(CancellationToken ct) =>
            both ??= AnalyticsQueries.SeriesAsync(db, userId, null, from.AddDays(-days), to, ct);
        return Render(
            "Analytics/Index",
            new Dictionary<string, object?>
            {
                ["range"] = days,
                ["summary"] = Defer(
                    async ct =>
                        AnalyticsQueries.Summary(
                            await Both(ct),
                            from,
                            await db.Posts.CountAsync(p => p.AuthorId == userId, ct)
                        ),
                    group: "summary"
                ),
                ["series"] = Defer(
                    async ct =>
                        (await Both(ct)).Where(r => r.Date.CompareTo(fromText) >= 0).ToArray(),
                    group: "charts"
                ),
                ["topPosts"] = Defer(
                    ct => AnalyticsQueries.TopPostsAsync(db, userId, from, to, ct),
                    group: "charts"
                ),
            }
        );
    }

    internal static async Task<IResult> PostAnalyticsAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        int? range,
        CancellationToken ct
    )
    {
        var userId = ctx.User.UserId()!.Value;
        var post = await db
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
        if (post is null)
            return ErrorPage.NotFound();
        if (post.AuthorId != userId)
            return ErrorPage.Forbidden();
        var days = ClampRange(range);
        var (from, to) = Window(days);
        var fromText = from.ToString("yyyy-MM-dd");
        Task<DayRow[]>? both = null;
        Task<DayRow[]> Both(CancellationToken c) =>
            both ??= AnalyticsQueries.SeriesAsync(db, userId, id, from.AddDays(-days), to, c);
        return Render(
            "Posts/Analytics",
            new Dictionary<string, object?>
            {
                ["post"] = new
                {
                    post.Id,
                    post.Title,
                    post.CreatedAt,
                    post.ViewCount,
                    post.LikeCount,
                    post.CommentCount,
                },
                ["range"] = days,
                ["summary"] = Defer(
                    async c => AnalyticsQueries.Summary(await Both(c), from, 1),
                    group: "summary"
                ),
                ["series"] = Defer(
                    async c =>
                        (await Both(c)).Where(r => r.Date.CompareTo(fromText) >= 0).ToArray(),
                    group: "charts"
                ),
            }
        );
    }
}

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

    public async Task TrackAsync(HttpContext ctx, Guid postId, Guid authorId, CancellationToken ct)
    {
        try
        {
            var userId = ctx.User.UserId();
            if (userId == authorId)
                return;
            var ua = ctx.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(ua) || BotRegex().IsMatch(ua))
                return;
            var h = ctx.Request.Headers;
            if (
                h["Purpose"] == "prefetch"
                || h["Sec-Purpose"].ToString().StartsWith("prefetch")
                || h.ContainsKey("X-Inertia-Prefetch")
            )
                return;
            var day = DateOnly.FromDateTime(DateTime.UtcNow);
            var dayText = day.ToString("yyyy-MM-dd");
            var identity = userId?.ToString() ?? $"{ctx.Connection.RemoteIpAddress}|{ua}";
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
