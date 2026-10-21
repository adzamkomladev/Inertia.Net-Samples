namespace VuePgBlog.Features;

public static class Alerts
{
    public static void MapAlerts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account/alerts").RequireAuthorization("verified");
        group.MapGet("/", AlertsService.InboxAsync);
        group.MapPost("/read-all", AlertsService.ReadAllAsync);
        group.MapPost("/{id:guid}/read", AlertsService.ReadAsync);
    }
}
