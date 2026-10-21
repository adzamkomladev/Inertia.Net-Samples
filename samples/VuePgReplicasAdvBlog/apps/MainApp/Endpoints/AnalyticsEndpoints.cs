using Microsoft.EntityFrameworkCore;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Analytics;
using VuePgReplicasAdvBlog.Infrastructure;
using static VuePgReplicasAdvBlog.Features.Analytics.AnalyticsHelpers;

namespace VuePgReplicasAdvBlog.MainApp.Endpoints;

public static class AnalyticsEndpoints
{
    static readonly TimeSpan AnalyticsTtl = TimeSpan.FromSeconds(60);

    public static void MapAnalytics(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/analytics").RequireAuthorization();
        group.MapGet("/", Index);
        group.MapGet("/posts/{id:guid}", PostAsync);
    }

    static IResult Index(int? days, HttpContext ctx, AppDb db)
    {
        var n = Days(days);
        var since = Today.AddDays(1 - n);
        var uid = ctx.User.UserId();
        var stats = AnalyticsService.RawStats(db, uid, null, since).AsNoTracking();
        return Render("Analytics/Index", new InertiaProps
        {
            ["days"] = n,
            ["kpis"] = Defer(async ct => await AnalyticsService.KpisAsync(stats, await db.Posts.CountAsync(p => p.AuthorId == uid, ct), ct), "kpis")
                .Cache($"analytics:{uid}:{n}:{since:yyyyMMdd}:kpis", AnalyticsTtl),
            ["series"] = Defer(ct => AnalyticsService.SeriesAsync(stats, since, n, ct), "charts")
                .Cache($"analytics:{uid}:{n}:{since:yyyyMMdd}:series", AnalyticsTtl),
            ["topPosts"] = Defer(ct => AnalyticsService.TopPostsAsync(stats, db, ct), "charts")
                .Cache($"analytics:{uid}:{n}:{since:yyyyMMdd}:topPosts", AnalyticsTtl)
        });
    }

    static async Task<IResult> PostAsync(Guid id, int? days, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var uid = ctx.User.UserId();
        var post = await AnalyticsService.GetPostAsync(id, uid, db, ct);
        var n = Days(days);
        var since = Today.AddDays(1 - n);
        var stats = AnalyticsService.RawStats(db, null, id, since).AsNoTracking();
        return Render("Analytics/Post", new InertiaProps
        {
            ["days"] = n,
            ["post"] = new { post.Id, post.Title, post.Slug },
            ["kpis"] = Defer(c => AnalyticsService.KpisAsync(stats, 1, c), "kpis")
                .Cache($"analytics-post:{uid}:{id}:{n}:{since:yyyyMMdd}:kpis", AnalyticsTtl),
            ["series"] = Defer(c => AnalyticsService.SeriesAsync(stats, since, n, c), "charts")
                .Cache($"analytics-post:{uid}:{id}:{n}:{since:yyyyMMdd}:series", AnalyticsTtl),
            ["referrers"] = Defer(c => AnalyticsService.ReferrersAsync(id, since, db, c), "charts")
                .Cache($"analytics-post:{uid}:{id}:{n}:{since:yyyyMMdd}:referrers", AnalyticsTtl)
        });
    }
}
