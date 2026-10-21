using Microsoft.EntityFrameworkCore;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Features.Analytics;

public static class AnalyticsService
{
    public static IQueryable<PostDailyStat> RawStats(
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

    public static Task<int> PostCountAsync(AppDb db, Guid userId, CancellationToken ct) =>
        db.Posts.CountAsync(p => p.AuthorId == userId, ct);

    public static async Task<Kpis> KpisAsync(
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

    public static async Task<List<SeriesPoint>> SeriesAsync(
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

    public static async Task<List<TopPost>> TopPostsAsync(IQueryable<PostDailyStat> stats, AppDb db, CancellationToken ct)
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
    }

    public static async Task<AnalyticsPost> GetPostAsync(Guid id, Guid userId, AppDb db, CancellationToken ct)
    {
        var post = await db.Posts.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new AnalyticsPost(p.Id, p.Title, p.Slug, p.AuthorId)).FirstOrDefaultAsync(ct);
        if (post is null) throw new FeatureException(FailureKind.Missing);
        if (post.AuthorId != userId) throw new FeatureException(FailureKind.AccessDenied);
        return post;
    }

    public static async Task<List<Referrer>> ReferrersAsync(Guid id, DateOnly since, AppDb db, CancellationToken ct)
    {
        var fromAt = since.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rows = await db.PostViews.AsNoTracking().Where(v => v.PostId == id && v.At >= fromAt)
            .GroupBy(v => v.Referrer).Select(g => new { Host = g.Key, Views = g.Count() })
            .OrderByDescending(x => x.Views).Take(10).ToListAsync(ct);
        return rows.Select(x => new Referrer(x.Host ?? "Direct", x.Views)).ToList();
    }
}
