using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using TechLandier.Features;

namespace TechLandier.Infrastructure;

// No surface functions for clients to call: connections only receive pushed updates.
public sealed class StoreHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole("admin") == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, "admins");
        await base.OnConnectedAsync();
    }
}

public sealed record OrderPulse(string OrderNumber, string Status, string PaymentStatus);

/// Server-to-client pushes. Delivery is best effort: a failure is logged, never thrown into the request.
// ponytail: process-local; add a SignalR backplane and a durable inbox for multiple instances or offline users.
public sealed class RealtimeEvents(IHubContext<StoreHub> hub, ILogger<RealtimeEvents> logger)
{
    IClientProxy Admins => hub.Clients.Group("admins");
    IClientProxy User(Guid userId) => hub.Clients.User(userId.ToString());

    /// Admin dashboards refresh their KPI strip whenever any order changes shape.
    public Task OrderChangedAsync(Order order, CancellationToken ct = default) =>
        Send(Admins, "OrderChanged", new OrderPulse(order.Number, order.Status, order.PaymentStatus), ct);

    /// Per-user inbox push (Alert rows are the durable copy).
    public Task NotifyUserAsync(Guid userId, string title, string body, string? href, CancellationToken ct = default) =>
        Send(User(userId), "Notification", new { title, body, href }, ct);

    /// Nudges a customer's open tracking/order pages to reload the timeline.
    public Task OrderTimelineUpdatedAsync(Guid userId, string orderNumber, CancellationToken ct = default) =>
        Send(User(userId), "OrderTimelineUpdated", new { orderNumber }, ct);

    public Task StockAlertAsync(Guid productId, int stock, CancellationToken ct = default) =>
        Send(Admins, "StockAlert", new { productId, stock }, ct);

    /// Generic admin busy-light (SLA breaches, escalation pulses).
    public Task AdminPulseAsync(string title, string body, string? href, CancellationToken ct = default) =>
        Send(Admins, "Notification", new { title, body, href }, ct);

    async Task Send(IClientProxy clients, string method, object payload, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try { await clients.SendAsync(method, payload, timeout.Token); }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime {Method} failed", method); }
    }
}

/// In-process "one alert per crossing" dedupe so low-stock pings fire once per dip, not per sale.
// ponytail: per-instance; move into the checkout transaction if the app ever runs on several instances.
public sealed class StockAlerts
{
    readonly ConcurrentDictionary<Guid, bool> dipping = new();

    /// <summary>True exactly once each time stock falls below the threshold; resets on restock.</summary>
    public bool ShouldAlert(Guid productId, int stock, int threshold)
    {
        if (stock >= threshold)
        {
            dipping.TryRemove(productId, out _);
            return false;
        }
        return dipping.TryAdd(productId, true);
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
