using Microsoft.EntityFrameworkCore;
using ReactSqliteReplicasBlog.Data;
using ReactSqliteReplicasBlog.Infrastructure;
using static ReactSqliteReplicasBlog.Features.Analytics.AnalyticsHelpers;

namespace ReactSqliteReplicasBlog.Features.Analytics;

public static class AnalyticsFeature
{
    // One skipped query per prop: page visits reload these within a minute far more often than the day rows change.
    // The key carries the day window (fromText), so the new window after midnight is a different entry.
    static readonly TimeSpan AnalyticsTtl = TimeSpan.FromSeconds(60);

    public static IServiceCollection AddAnalyticsFeature(this IServiceCollection services)
    {
        services.AddScoped<ViewTracker>();
        return services;
    }

    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/analytics", Overview).RequireAuthorization();
        app.MapGet("/posts/{id:guid}/analytics", PostAnalyticsAsync).RequireAuthorization();
        return app;
    }

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
                // Cached props (Inertia.Net 1.4): the value is kept as JSON in the app's IMemoryCache, so the loader runs
                // at most once a minute per user, range and day. Analytics dashboards are read far more often than the
                // underlying day rows change.
                ["summary"] = Defer(
                    async ct =>
                        AnalyticsQueries.Summary(
                            await Both(ct),
                            from,
                            await db.Posts.CountAsync(p => p.AuthorId == userId, ct)
                        ),
                    group: "summary"
                ).Cache($"analytics:{userId}:{days}:{fromText}:summary", AnalyticsTtl),
                ["series"] = Defer(
                    async ct => AnalyticsQueries.CurrentRows(await Both(ct), fromText),
                    group: "charts"
                ).Cache($"analytics:{userId}:{days}:{fromText}:series", AnalyticsTtl),
                ["topPosts"] = Defer(
                    ct => AnalyticsQueries.TopPostsAsync(db, userId, from, to, ct),
                    group: "charts"
                ).Cache($"analytics:{userId}:{days}:{fromText}:topPosts", AnalyticsTtl),
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
        var post = await AnalyticsQueries.PostHeaderAsync(db, id, ct);
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
                ).Cache($"analytics-post:{userId}:{id}:{days}:{fromText}:summary", AnalyticsTtl),
                ["series"] = Defer(
                    async c => AnalyticsQueries.CurrentRows(await Both(c), fromText),
                    group: "charts"
                ).Cache($"analytics-post:{userId}:{id}:{days}:{fromText}:series", AnalyticsTtl),
            }
        );
    }
}
