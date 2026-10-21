using Microsoft.AspNetCore.SignalR;

namespace CarParker.Infrastructure;

// No client-callable methods: connections receive persisted updates only.
// SignalR groups come from role claims: admin/guard connections join "staff" for board pushes.
public sealed class ParkHub : Hub
{
    public const string StaffGroup = "staff";

    public override Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user?.IsInRole("admin") == true || user?.IsInRole("guard") == true)
            Groups.AddToGroupAsync(Context.ConnectionId, StaffGroup);
        return base.OnConnectedAsync();
    }
}

/// <summary>SpotChanged payload broadcast to everyone watching a lot.</summary>
public sealed record SpotChangeEvent(Guid LotId, Guid SpotId, string State);

/// <summary>Best-effort pushes: a failed delivery is logged and never fails the request that caused it.</summary>
public sealed class RealtimeEvents(IHubContext<ParkHub> hub, ILogger<RealtimeEvents> logger)
{
    // ponytail: process-local transient delivery; add a backplane and durable inbox for multiple instances/offline notifications.
    public async Task SpotChangedAsync(Guid lotId, Guid spotId, string state, CancellationToken ct = default)
    {
        await SendAsync(hub.Clients.All, "SpotChanged", new SpotChangeEvent(lotId, spotId, state), ct);
        await LotChangedAsync(lotId, ct);
    }

    public Task LotChangedAsync(Guid lotId, CancellationToken ct = default) => SendAsync(hub.Clients.All, "LotPulse", new { lotId }, ct);

    /// <summary>kind: session | refund | incident | claim</summary>
    public Task BoardChangedAsync(string kind, CancellationToken ct = default) =>
        SendAsync(hub.Clients.Group(ParkHub.StaffGroup), "BoardChanged", new { kind }, ct);

    public Task NotifyUserAsync(Guid userId, string message, string? href = null, CancellationToken ct = default) =>
        SendAsync(hub.Clients.User(userId.ToString()), "Notification", new { message, href }, ct);

    public Task NotifyStaffAsync(string message, string? href = null, CancellationToken ct = default) =>
        SendAsync(hub.Clients.Group(ParkHub.StaffGroup), "Notification", new { message, href }, ct);

    async Task SendAsync(IClientProxy clients, string method, object payload, CancellationToken ct)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await clients.SendAsync(method, payload, ct == default ? timeout.Token : ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime {Method} delivery failed", method); }
    }
}

public sealed class RealtimeOriginMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/realtime"))
        {
            var origin = context.Request.Headers.Origin.ToString();
            // Proxies can forward ws/wss; browser Origin uses the page's http/https scheme.
            var originScheme = context.Request.Scheme.ToLowerInvariant() switch
            {
                "wss" => "https",
                "ws" => "http",
                var scheme => scheme
            };
            var unsafeMethod = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method);
            // CORS does not protect WebSockets. Cookie-authenticated transport accepts only same-origin browser writes.
            if ((unsafeMethod && origin.Length == 0) || (origin.Length > 0 &&
                !string.Equals(origin, originScheme + "://" + context.Request.Host, StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }
        await next(context);
    }
}
