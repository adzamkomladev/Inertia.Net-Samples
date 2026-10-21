using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Infrastructure;
using static VuePgBlog.Features.AnalyticsHelpers;

namespace VuePgBlog.Features;

public static partial class AnalyticsService
{
    // One skipped aggregation per prop: page visits reload these within a minute far more often than new events land.
    // The key carries the day window, so the new window after midnight is a different entry.
    static readonly TimeSpan AnalyticsTtl = TimeSpan.FromSeconds(60);

    internal static IQueryable<PostDailyStat> RawStats(
        AppDb db,
        Guid? authorId,
        Guid? postId,
        DateOnly since
    ) =>
        db.Database.SqlQuery<PostDailyStat>(
            $"""
            SELECT "PostId", CAST("At" AS date) AS "Date", CAST(COUNT(*) AS integer) AS "Views", CAST(COUNT(DISTINCT "ViewerHash") AS integer) AS "UniqueViewers", 0 AS "Likes", 0 AS "Comments"
            FROM "PostViews" WHERE "At" >= {since.ToDateTime(
                TimeOnly.MinValue,
                DateTimeKind.Utc
            )} AND (CAST({postId} AS uuid) IS NULL OR "PostId" = CAST({postId} AS uuid))
              AND (CAST({authorId} AS uuid) IS NULL OR "PostId" IN (SELECT "Id" FROM "Posts" WHERE "AuthorId" = CAST({authorId} AS uuid)))
            GROUP BY "PostId", CAST("At" AS date)
            UNION ALL
            SELECT "PostId", CAST("CreatedAt" AS date) AS "Date", 0 AS "Views", 0 AS "UniqueViewers", CAST(COUNT(*) AS integer) AS "Likes", 0 AS "Comments"
            FROM "Likes" WHERE "CreatedAt" >= {since.ToDateTime(
                TimeOnly.MinValue,
                DateTimeKind.Utc
            )} AND (CAST({postId} AS uuid) IS NULL OR "PostId" = CAST({postId} AS uuid))
              AND (CAST({authorId} AS uuid) IS NULL OR "PostId" IN (SELECT "Id" FROM "Posts" WHERE "AuthorId" = CAST({authorId} AS uuid))) GROUP BY "PostId", CAST("CreatedAt" AS date)
            UNION ALL
            SELECT "PostId", CAST("CreatedAt" AS date) AS "Date", 0 AS "Views", 0 AS "UniqueViewers", 0 AS "Likes", CAST(COUNT(*) AS integer) AS "Comments"
            FROM "Comments" WHERE "CreatedAt" >= {since.ToDateTime(
                TimeOnly.MinValue,
                DateTimeKind.Utc
            )} AND (CAST({postId} AS uuid) IS NULL OR "PostId" = CAST({postId} AS uuid))
              AND (CAST({authorId} AS uuid) IS NULL OR "PostId" IN (SELECT "Id" FROM "Posts" WHERE "AuthorId" = CAST({authorId} AS uuid))) GROUP BY "PostId", CAST("CreatedAt" AS date)
            """
        );

    internal static async Task<Kpis> KpisAsync(
        IQueryable<PostDailyStat> stats,
        int posts,
        CancellationToken ct
    )
    {
        var t = await stats
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Views = g.Sum(s => s.Views),
                Unique = g.Sum(s => s.UniqueViewers),
                Likes = g.Sum(s => s.Likes),
                Comments = g.Sum(s => s.Comments),
            })
            .FirstOrDefaultAsync(ct);
        // Unique viewers is the sum of daily uniques: the same visitor on two days counts twice (hashes rotate daily by design).
        return new Kpis(t?.Views ?? 0, t?.Unique ?? 0, t?.Likes ?? 0, t?.Comments ?? 0, posts);
    }

    internal static async Task<List<SeriesPoint>> SeriesAsync(
        IQueryable<PostDailyStat> stats,
        DateOnly from,
        int days,
        CancellationToken ct
    )
    {
        var byDate = (
            await stats
                .GroupBy(s => s.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Views = g.Sum(s => s.Views),
                    Unique = g.Sum(s => s.UniqueViewers),
                    Likes = g.Sum(s => s.Likes),
                    Comments = g.Sum(s => s.Comments),
                })
                .ToListAsync(ct)
        ).ToDictionary(x => x.Date);
        return Enumerable
            .Range(0, days)
            .Select(i =>
            {
                var d = from.AddDays(i);
                return byDate.TryGetValue(d, out var x)
                    ? new SeriesPoint(
                        d.ToString("yyyy-MM-dd"),
                        x.Views,
                        x.Unique,
                        x.Likes,
                        x.Comments
                    )
                    : new SeriesPoint(d.ToString("yyyy-MM-dd"), 0, 0, 0, 0);
            })
            .ToList();
    }

    internal static IResult Get(int? days, HttpContext ctx, AppDb db)
    {
        var n = Days(days);
        var since = Today.AddDays(1 - n);
        var uid = ctx.User.UserId();
        var stats = RawStats(db, uid, null, since).AsNoTracking();
        return Render(
            "Analytics/Index",
            new InertiaProps
            {
                ["days"] = n,
                // Cached props (Inertia.Net 1.4): the value is kept as JSON in the app's IMemoryCache, so the loader runs
                // at most once a minute per user, range and day window.
                ["kpis"] = Defer(
                    async ct =>
                        await KpisAsync(
                            stats,
                            await db.Posts.CountAsync(p => p.AuthorId == uid, ct),
                            ct
                        ),
                    "kpis"
                ).Cache($"analytics:{uid}:{n}:{since:yyyyMMdd}:kpis", AnalyticsTtl),
                ["series"] = Defer(ct => SeriesAsync(stats, since, n, ct), "charts")
                    .Cache($"analytics:{uid}:{n}:{since:yyyyMMdd}:series", AnalyticsTtl),
                ["topPosts"] = Defer(
                    async ct =>
                    {
                        // Aggregate first, then look titles up separately to keep the query simple across providers.
                        var agg = await stats
                            .GroupBy(s => s.PostId)
                            .Select(g => new
                            {
                                Id = g.Key,
                                Views = g.Sum(x => x.Views),
                                Likes = g.Sum(x => x.Likes),
                                Comments = g.Sum(x => x.Comments),
                            })
                            .OrderByDescending(x => x.Views)
                            .ThenByDescending(x => x.Likes)
                            .Take(10)
                            .ToListAsync(ct);
                        var ids = agg.Select(x => x.Id).ToList();
                        var posts = await db
                            .Posts.AsNoTracking()
                            .Where(p => ids.Contains(p.Id))
                            .Select(p => new
                            {
                                p.Id,
                                p.Slug,
                                p.Title,
                            })
                            .ToDictionaryAsync(p => p.Id, ct);
                        return agg.Where(x => posts.ContainsKey(x.Id))
                            .Select(x => new TopPost(
                                x.Id,
                                posts[x.Id].Slug,
                                posts[x.Id].Title,
                                x.Views,
                                x.Likes,
                                x.Comments
                            ))
                            .ToList();
                    },
                    "charts"
                ).Cache($"analytics:{uid}:{n}:{since:yyyyMMdd}:topPosts", AnalyticsTtl),
            }
        );
    }

    internal static async Task<IResult> FeedAsync(
        Guid id,
        int? days,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var post = await db
            .Posts.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Slug,
                p.AuthorId,
            })
            .FirstOrDefaultAsync(ct);
        if (post is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        var uid = ctx.User.UserId();
        if (post.AuthorId != uid)
            return HttpExtensions.Forbidden();
        var n = Days(days);
        var since = Today.AddDays(1 - n);
        var fromAt = since.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var stats = RawStats(db, null, id, since).AsNoTracking();
        return Render(
            "Analytics/Post",
            new InertiaProps
            {
                ["days"] = n,
                ["post"] = new
                {
                    post.Id,
                    post.Title,
                    post.Slug,
                },
                ["kpis"] = Defer(c => KpisAsync(stats, 1, c), "kpis")
                    .Cache($"analytics-post:{uid}:{id}:{n}:{since:yyyyMMdd}:kpis", AnalyticsTtl),
                ["series"] = Defer(c => SeriesAsync(stats, since, n, c), "charts")
                    .Cache($"analytics-post:{uid}:{id}:{n}:{since:yyyyMMdd}:series", AnalyticsTtl),
                ["referrers"] = Defer(
                    async c =>
                        (
                            await db
                                .PostViews.AsNoTracking()
                                .Where(v => v.PostId == id && v.At >= fromAt)
                                .GroupBy(v => v.Referrer)
                                .Select(g => new { Host = g.Key, Views = g.Count() })
                                .OrderByDescending(x => x.Views)
                                .Take(10)
                                .ToListAsync(c)
                        )
                            .Select(x => new Referrer(x.Host ?? "Direct", x.Views))
                            .ToList(),
                    "charts"
                ).Cache($"analytics-post:{uid}:{id}:{n}:{since:yyyyMMdd}:referrers", AnalyticsTtl),
            }
        );
    }
}
