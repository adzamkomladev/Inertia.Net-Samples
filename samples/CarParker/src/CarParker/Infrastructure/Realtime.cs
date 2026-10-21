using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using CarParker.Data;

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

public sealed class RealtimeEvents(IServiceScopeFactory scopes, IHubContext<ParkHub> hub, ILogger<RealtimeEvents> logger)
{
    // ponytail: process-local transient delivery; add a backplane and durable inbox for multiple instances/offline notifications.
    public async Task SpotChangedAsync(Guid lotId, Guid spotId, string state, CancellationToken ct = default)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await hub.Clients.All.SendAsync("SpotChanged", new SpotChangeEvent(lotId, spotId, state), ct == default ? timeout.Token : ct);
            await hub.Clients.All.SendAsync("LotPulse", new { lotId }, ct == default ? timeout.Token : ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime spot delivery failed for lot {LotId}", lotId); }
    }

    public async Task SpotBatchChangedAsync(Dictionary<Guid, string> spots, Guid lotId, CancellationToken ct = default)
    {
        try
        {
            foreach (var (spotId, state) in spots)
                await hub.Clients.All.SendAsync("SpotChanged", new SpotChangeEvent(lotId, spotId, state), ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime spot batch delivery failed for lot {LotId}", lotId); }
    }

    public async Task BoardChangedAsync(string kind, CancellationToken ct = default)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            // kind: session | refund | incident | claim | alert
            await hub.Clients.Group(ParkHub.StaffGroup).SendAsync("BoardChanged", new { kind }, ct == default ? timeout.Token : ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime board push failed (kind {Kind})", kind); }
    }

    public async Task NotifyUserAsync(Guid userId, string message, string? href = null, CancellationToken ct = default)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await hub.Clients.User(userId.ToString()).SendAsync("Notification", new { message, href }, ct == default ? timeout.Token : ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime user notification failed for user {UserId}", userId); }
    }

    public async Task NotifyStaffAsync(string message, string? href = null, CancellationToken ct = default)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await hub.Clients.Group(ParkHub.StaffGroup).SendAsync("Notification", new { message, href }, ct == default ? timeout.Token : ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime staff notification failed"); }
    }

    /// <summary>Lot document changed (create/edit/block/mass restate): let connected visitors know to re-pull.</summary>
    public async Task LotChangedAsync(Guid lotId, CancellationToken ct = default)
    {
        try
        {
            await hub.Clients.All.SendAsync("LotPulse", new { lotId }, ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime lot pulse failed for lot {LotId}", lotId); }
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
