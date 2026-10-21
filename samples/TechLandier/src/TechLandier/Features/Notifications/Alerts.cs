using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Alerts
{
    public static void MapAlerts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account/alerts").RequireAuthorization("verified");
        group.MapGet("/", InboxAsync);
        group.MapPost("/read-all", ReadAllAsync);
        group.MapPost("/{id:guid}/read", ReadAsync);
    }

    internal static async Task<IResult> InboxAsync(int? page, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var (alerts, unreadCount) = await AlertsService.GetInboxAsync(ctx.User.UserId(), page, db, ct);
        return Render("account/Alerts", new InertiaProps
        {
            ["alerts"] = alerts,
            ["unreadCount"] = unreadCount,
        });
    }

    internal static async Task<IResult> ReadAsync(Guid id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        await AlertsService.MarkReadAsync(ctx.User.UserId(), id, db, ct);
        return Back();
    }

    internal static async Task<IResult> ReadAllAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        await AlertsService.MarkAllReadAsync(ctx.User.UserId(), db, ct);
        return Back().WithFlash("toast", new Toast("success", "All notifications marked as read."));
    }
}
