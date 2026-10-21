using System.Security.Claims;
using Inkwell.Infrastructure;

namespace Inkwell.Features.Analytics;

public static class AnalyticsFeature
{
    // Dashboards are reloaded far more often than the day rows change. Keys carry every input: user, post, range
    // and the first day of the window, so the window moving at midnight is a new entry.
    static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/analytics", Overview).RequireAuthorization();
        app.MapGet("/posts/{id:guid}/analytics", PostAnalytics).RequireAuthorization();
        return app;
    }

    static IResult Overview(int? range, AnalyticsService analytics, ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = user.UserId()!.Value;
        var days = AnalyticsService.ClampRange(range);
        var (from, to) = analytics.Window(days);
        var key = $"analytics:{userId}:{days}:{from:yyyy-MM-dd}";
        // The previous and current period are fetched once and shared by "summary" and "series".
        var daily = new Lazy<Task<DayRow[]>>(() => analytics.DailyAsync(userId, null, from.AddDays(-days), to, ct));
        return Render("Analytics/Index", new InertiaProps
        {
            ["range"] = days,
            ["summary"] = Defer(async _ =>
                {
                    var posts = analytics.PostCountAsync(userId, ct);
                    return AnalyticsService.Summarize(await daily.Value, from, await posts);
                }, group: "summary")
                .Cache($"{key}:summary", CacheFor).Parallel(),
            ["series"] = Defer(async _ => AnalyticsService.CurrentRows(await daily.Value, from), group: "charts")
                .Cache($"{key}:series", CacheFor).Parallel(),
            ["topPosts"] = Defer(_ => analytics.TopPostsAsync(userId, from, to, ct), group: "charts")
                .Cache($"{key}:topPosts", CacheFor).Parallel(),
        });
    }

    static async Task<IResult> PostAnalytics(Guid id, int? range, AnalyticsService analytics, ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = user.UserId()!.Value;
        var post = await analytics.PostHeaderAsync(id, ct);
        if (post is null) return ErrorPage.NotFound();
        if (post.AuthorId != userId) return ErrorPage.Forbidden();
        var days = AnalyticsService.ClampRange(range);
        var (from, to) = analytics.Window(days);
        var key = $"analytics-post:{userId}:{id}:{days}:{from:yyyy-MM-dd}";
        var daily = new Lazy<Task<DayRow[]>>(() => analytics.DailyAsync(userId, id, from.AddDays(-days), to, ct));
        return Render("Posts/Analytics", new InertiaProps
        {
            ["post"] = new { post.Id, post.Title, post.CreatedAt, post.ViewCount, post.LikeCount, post.CommentCount },
            ["range"] = days,
            ["summary"] = Defer(async _ => AnalyticsService.Summarize(await daily.Value, from, 1), group: "summary")
                .Cache($"{key}:summary", CacheFor).Parallel(),
            ["series"] = Defer(async _ => AnalyticsService.CurrentRows(await daily.Value, from), group: "charts")
                .Cache($"{key}:series", CacheFor).Parallel(),
        });
    }
}
