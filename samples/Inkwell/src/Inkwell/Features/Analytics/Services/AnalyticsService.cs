using Inkwell.Data;
using Inkwell.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Features.Analytics;

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

    /// <summary>Zero-filled views, likes and comments for every day in [from, to].</summary>
    public async Task<DayRow[]> DailyAsync(Guid userId, Guid? postId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var db = await dbs.CreateDbContextAsync(ct);
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var posts = db.Posts.IgnoreQueryFilters()
            .Where(p => p.AuthorId == userId && (postId == null || p.Id == postId)).Select(p => p.Id);
        var views = await db.PostViews.Where(v => posts.Contains(v.PostId) && v.Day >= from && v.Day <= to)
            .GroupBy(v => v.Day).Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Day, g => g.Count, ct);
        var likes = await db.PostLikes.Where(l => posts.Contains(l.PostId) && l.CreatedAt >= start && l.CreatedAt < end)
            .GroupBy(l => DateOnly.FromDateTime(l.CreatedAt)).Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Day, g => g.Count, ct);
        var comments = await db.Comments.IgnoreQueryFilters()
            .Where(c => posts.Contains(c.PostId) && c.CreatedAt >= start && c.CreatedAt < end)
            .GroupBy(c => DateOnly.FromDateTime(c.CreatedAt)).Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Day, g => g.Count, ct);
        return Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i))
            .Select(day => new DayRow(Day(day), views.GetValueOrDefault(day), likes.GetValueOrDefault(day), comments.GetValueOrDefault(day)))
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
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return await db.Posts.IgnoreQueryFilters().Where(p => p.AuthorId == userId)
            .Select(p => new
            {
                p.Id, p.Title, p.CreatedAt, p.ViewCount,
                Views = db.PostViews.Count(v => v.PostId == p.Id && v.Day >= from && v.Day <= to),
                Likes = db.PostLikes.Count(l => l.PostId == p.Id && l.CreatedAt >= start && l.CreatedAt < end),
                Comments = db.Comments.IgnoreQueryFilters().Count(c => c.PostId == p.Id && c.CreatedAt >= start && c.CreatedAt < end),
            })
            .OrderByDescending(p => p.Views).ThenByDescending(p => p.CreatedAt).Take(10)
            .Select(p => new TopPostDto(p.Id, p.Title, p.Views, p.Likes, p.Comments, p.ViewCount))
            .ToArrayAsync(ct);
    }

    static string Day(DateOnly d) => d.ToString("yyyy-MM-dd");

}
