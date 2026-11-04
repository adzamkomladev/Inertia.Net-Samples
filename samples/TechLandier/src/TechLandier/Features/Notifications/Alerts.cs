using System.Security.Claims;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Alerts
{
    public static void MapAlerts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account/alerts").RequireAuthorization("verified");
        group.MapGet("/", async (PageRequest page, ClaimsPrincipal user, AlertsService svc, CancellationToken ct) =>
        {
            var (alerts, unread) = await svc.InboxAsync(user.UserId(), page, ct);
            // The open inbox follows the owner's channel, like the shared bell: the event reloads the
            // visible page after the cache entry was removed, so rows and the unread count stay fresh.
            return Render("account/Alerts", new InertiaProps
            {
                ["alerts"] = Live(alerts, UserEvents.Notification(user.UserId())),
                ["unreadCount"] = Always(unread).Live(UserEvents.Notification(user.UserId())),
            });
        });
        group.MapPost("/read-all", async (ClaimsPrincipal user, AlertsService svc, CancellationToken ct) =>
        {
            await svc.MarkAllReadAsync(user.UserId(), ct);
            return Back().WithFlash("toast", new Toast("success", "All notifications marked as read."));
        });
        group.MapPost("/{id:guid}/read", async (Guid id, ClaimsPrincipal user, AlertsService svc, CancellationToken ct) =>
        {
            await svc.MarkReadAsync(user.UserId(), id, ct);
            return Back();
        });
    }
}
