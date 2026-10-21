using MongoDB.Bson;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Infrastructure;
using static SvelteMongoReplicasBlog.Features.AnalyticsHelpers;

namespace SvelteMongoReplicasBlog.Features;

public static class Analytics
{
    // One skipped aggregation per prop: page visits reload these within a minute far more often than new events land.
    // The key carries the day window, so the new window after midnight is a different entry.
    static readonly TimeSpan AnalyticsTtl = TimeSpan.FromSeconds(60);

    public static void MapAnalytics(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/analytics").RequireAuthorization();
        group.MapGet("/", Get);
        group.MapGet("/posts/{id:guid}", FeedAsync);
    }

    internal static IResult Get(int? days, HttpContext ctx, AppDb db)
    {
        var n = Days(days);
        var since = Today.AddDays(1 - n);
        var uid = ctx.User.UserId();
        return Render(
            "Analytics/Index",
            new InertiaProps
            {
                ["days"] = n,
                // Cached props (Inertia.Net 1.4): the value is kept as JSON in the app's IMemoryCache, so the loader runs
                // at most once a minute per user, range and day window.
                ["kpis"] = Defer(
                    async ct => await AnalyticsService.KpisDataAsync(db, uid, since, ct),
                    "kpis"
                ).Cache($"analytics:{uid}:{n}:{since:yyyyMMdd}:kpis", AnalyticsTtl),
                ["series"] = Defer(
                    async ct => await AnalyticsService.SeriesDataAsync(db, uid, since, n, ct),
                    "charts"
                ).Cache($"analytics:{uid}:{n}:{since:yyyyMMdd}:series", AnalyticsTtl),
                ["topPosts"] = Defer(
                    async ct => await AnalyticsService.TopPostsDataAsync(db, uid, since, ct),
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
        var post = await AnalyticsService.PostHeaderAsync(db, id, ct);
        if (post is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        var uid = ctx.User.UserId();
        if (post.AuthorId != uid)
            return HttpExtensions.Forbidden();
        var n = Days(days);
        var since = Today.AddDays(1 - n);
        var fromAt = AnalyticsService.Utc(since);
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
                ["kpis"] = Defer(
                    async c => await AnalyticsService.PostKpisDataAsync(db, id, since, c),
                    "kpis"
                ).Cache($"analytics-post:{uid}:{id}:{n}:{since:yyyyMMdd}:kpis", AnalyticsTtl),
                ["series"] = Defer(c => AnalyticsService.Series(db, [id], since, n, c), "charts")
                    .Cache($"analytics-post:{uid}:{id}:{n}:{since:yyyyMMdd}:series", AnalyticsTtl),
                ["referrers"] = Defer(
                    async c => await AnalyticsService.ReferrersDataAsync(db, id, fromAt, c),
                    "charts"
                ).Cache($"analytics-post:{uid}:{id}:{n}:{since:yyyyMMdd}:referrers", AnalyticsTtl),
            }
        );
    }
}
