using System.Security.Claims;
using Inkwell.Infrastructure;

namespace Inkwell.Features.Notifications;

public static class Notifications
{
    public static void MapNotifications(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/notifications").RequireAuthorization();
        group.MapGet("/", async (NotificationsService svc, ClaimsPrincipal user, CancellationToken ct) =>
            Render("Notifications/Index", new InertiaProps { ["items"] = await svc.ListAsync(user.UserId()!.Value, ct) }));
        group.MapPost("/read-all", async (NotificationsService svc, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await svc.MarkAllReadAsync(user.UserId()!.Value, ct);
            return Back();
        });
    }
}
