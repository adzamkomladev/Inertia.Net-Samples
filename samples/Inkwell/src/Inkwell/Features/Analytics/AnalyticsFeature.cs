using System.Security.Claims;
using Inkwell.Infrastructure;

namespace Inkwell.Features.Analytics;

public static class AnalyticsFeature
{
    // Keys carry every input: user, post, range and the first day of the window, so the window moving at midnight is
    // a new entry. AnalyticsCache also carries the tags that invalidate a user's dashboards after a post changes.

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
        var daily = new Lazy<Task<DayRow[]>>(async () => await analytics.DailyAsync(userId, null, from.AddDays(-days), to, ct));
        return Render("Analytics/Index", new InertiaProps
        {
            ["range"] = days,
            ["summary"] = Defer(async _ =>
                {
                    var posts = analytics.PostCountAsync(userId, ct);
                    return AnalyticsService.Summarize(await daily.Value, from, await posts);
                }, group: "summary")
                .Cache($"{key}:summary", AnalyticsCache.FreshFor, AnalyticsCache.StaleFor, [AnalyticsCache.UserTag(userId)]).Parallel(),
            ["series"] = Defer(async _ => AnalyticsService.CurrentRows(await daily.Value, from), group: "charts")
                .Cache($"{key}:series", AnalyticsCache.FreshFor, AnalyticsCache.StaleFor, [AnalyticsCache.UserTag(userId)]).Parallel(),
            ["topPosts"] = Defer(async _ => await analytics.TopPostsAsync(userId, from, to, ct), group: "charts")
                .Cache($"{key}:topPosts", AnalyticsCache.FreshFor, AnalyticsCache.StaleFor, [AnalyticsCache.UserTag(userId)]).Parallel(),
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
        var daily = new Lazy<Task<DayRow[]>>(async () => await analytics.DailyAsync(userId, id, from.AddDays(-days), to, ct));
        return Render("Posts/Analytics", new InertiaProps
        {
            ["post"] = new { post.Id, post.Title, post.CreatedAt, post.ViewCount, post.LikeCount, post.CommentCount },
            ["range"] = days,
            ["summary"] = Defer(async _ => AnalyticsService.Summarize(await daily.Value, from, 1), group: "summary")
                .Cache($"{key}:summary", AnalyticsCache.FreshFor, AnalyticsCache.StaleFor,
                    [AnalyticsCache.UserTag(userId), AnalyticsCache.PostTag(id)]).Parallel(),
            ["series"] = Defer(async _ => AnalyticsService.CurrentRows(await daily.Value, from), group: "charts")
                .Cache($"{key}:series", AnalyticsCache.FreshFor, AnalyticsCache.StaleFor,
                    [AnalyticsCache.UserTag(userId), AnalyticsCache.PostTag(id)]).Parallel(),
        });
    }
}
