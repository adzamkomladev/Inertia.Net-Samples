namespace VuePgBlog.Features;

public static class Analytics
{
    public static void MapAnalytics(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/analytics").RequireAuthorization();
        group.MapGet("/", AnalyticsService.Get);
        group.MapGet("/posts/{id:guid}", AnalyticsService.FeedAsync);
    }
}
