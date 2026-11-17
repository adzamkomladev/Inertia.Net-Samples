using System.Security.Claims;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Alerts
{
    public static void MapAlerts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account/alerts").RequireAuthorization("verified");
        group.MapGet("/", (PageRequest page, ClaimsPrincipal user, AlertsService svc, CancellationToken ct) =>
        {
            var uid = user.UserId();
            // The open inbox follows the owner's channel, like the shared bell. Both props are lazy, so an event
            // re-runs only these two queries (publish points removed the cache entry first), never the whole handler.
            return Render("account/Alerts", new InertiaProps
            {
                ["alerts"] = Prop(async c => await svc.InboxAsync(uid, page, c)).Live(UserEvents.Notification(uid)),
                ["unreadCount"] = Always(async c => await svc.UnreadCountAsync(uid, c)).Live(UserEvents.Notification(uid)),
            });
        }).WithName("account.alerts");
        group.MapPost("/read-all", async (ClaimsPrincipal user, AlertsService svc, CancellationToken ct) =>
        {
            await svc.MarkAllReadAsync(user.UserId(), ct);
            return Back().WithFlash("toast", new Toast("success", "All notifications marked as read."));
        }).WithName("account.alerts.read-all");
        group.MapPost("/{id:guid}/read", async (Guid id, ClaimsPrincipal user, AlertsService svc, CancellationToken ct) =>
        {
            await svc.MarkReadAsync(user.UserId(), id, ct);
            return Back();
        }).WithName("account.alerts.read");
    }
}
