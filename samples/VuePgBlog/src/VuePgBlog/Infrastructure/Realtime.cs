using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;

namespace VuePgBlog.Infrastructure;

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

public sealed class RealtimeEvents(IServiceScopeFactory scopes, IHubContext<StoreHub> hub, ILogger<RealtimeEvents> logger)
{
    // ponytail: process-local transient delivery; add a backplane and durable inbox for multiple instances/offline notifications.
    // ponytail: serialize snapshots globally; use per-order gates if event throughput becomes a bottleneck.
    readonly SemaphoreSlim gate = new(1, 1);

    /// Admin dashboards refresh their KPI strip whenever any order changes shape.
    public async Task OrderChangedAsync(Guid orderId, CancellationToken ct = default)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token).Token;
        var entered = false;
        try
        {
            await gate.WaitAsync(bounded);
            entered = true;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var order = await db.Orders.AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => new { o.Number, o.Status, o.PaymentStatus })
                .SingleOrDefaultAsync(bounded);
            if (order is null) return;
            await hub.Clients.Group("admins").SendAsync("OrderChanged", new OrderPulse(order.Number, order.Status, order.PaymentStatus), bounded);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime delivery failed for order {OrderId}", orderId); }
        finally { if (entered) gate.Release(); }
    }

    /// Per-user inbox push (Alert rows land persistently elsewhere).
    public async Task NotifyUserAsync(Guid userId, string title, string body, string? href, CancellationToken ct = default)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token).Token;
        try
        {
            await hub.Clients.User(userId.ToString())
                .SendAsync("Notification", new { title, body, href }, bounded);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime notification failed for user {UserId}", userId); }
    }

    /// Nudges a customer's open tracking/order pages to reload the timeline.
    public async Task OrderTimelineUpdatedAsync(Guid userId, string orderNumber, CancellationToken ct = default)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token).Token;
        try
        {
            await hub.Clients.User(userId.ToString())
                .SendAsync("OrderTimelineUpdated", new { orderNumber }, bounded);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime timeline push failed for user {UserId}", userId); }
    }

    /// Low-stock ping for the admin strip; call sites dedupe crossings via the StockAlerts singleton.
    public async Task StockAlertAsync(Guid productId, int stock, CancellationToken ct = default)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token).Token;
        try
        {
            await hub.Clients.Group("admins")
                .SendAsync("StockAlert", new { productId, stock }, bounded);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime stock alert failed for product {ProductId}", productId); }
    }

    /// Generic admin busy-light (SLA breaches, escalation pulses); durable copies land in admin Alerts elsewhere.
    public async Task AdminPulseAsync(string title, string body, string? href, CancellationToken ct = default)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token).Token;
        try
        {
            await hub.Clients.Group("admins").SendAsync("Notification", new { title, body, href }, bounded);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime admin pulse failed for {Title}", title); }
    }
}

/// In-process "one alert per crossing" dedupe so low-stock pings fire once per dip, not per sale.
// ponytail: move into the advisory-lock write path (or Redis) if the app ever scales past one instance.
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

    public void Reset(Guid productId) => dipping.TryRemove(productId, out _);
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
