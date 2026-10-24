using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TechLandier.Data;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
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

/// A queued server-to-client push. The worker's handler publishes it through the shared SignalR backplane.
public sealed record RealtimePush(string Method, JsonElement Payload, string? Group = null, string? UserId = null) : IJob;

/// <summary>
/// Produces realtime work. It never touches a hub directly, so the web role can run without
/// SignalR and every replica can enqueue for the worker.
/// </summary>
public sealed class RealtimeEvents(IServiceScopeFactory scopes, JobQueue jobs, IPropCacheInvalidator cache)
{
    /// Admin dashboards refresh their KPI strip whenever any order changes shape.
    public async Task OrderChangedAsync(TechLandier.Features.Order order, CancellationToken ct = default) =>
        await Send("admins", null, "OrderChanged", new OrderPulse(order.Number, order.Status, order.PaymentStatus), ct);

    /// Per-user inbox push (Alert rows are the durable copy).
    public async Task NotifyUserAsync(Guid userId, string title, string body, string? href, CancellationToken ct = default) =>
        await Send(null, userId.ToString(), "Notification", new { title, body, href }, ct);

    /// Nudges a customer's open tracking/order pages to reload the timeline.
    public async Task OrderTimelineUpdatedAsync(Guid userId, string orderNumber, CancellationToken ct = default) =>
        await Send(null, userId.ToString(), "OrderTimelineUpdated", new { orderNumber }, ct);

    public async Task StockAlertAsync(Guid productId, int stock, CancellationToken ct = default) =>
        await Send("admins", null, "StockAlert", new { productId, stock }, ct);

    /// Generic admin busy-light (SLA breaches, escalation pulses).
    public async Task AdminPulseAsync(string title, string body, string? href, CancellationToken ct = default) =>
        await Send("admins", null, "Notification", new { title, body, href }, ct);

    async Task Send(string? group, string? userId, string method, object payload, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        List<Guid>? admins = null;
        if (group == "admins" && method is "Notification" or "OrderChanged" or "StockAlert")
        {
            var notice = JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var (title, body, href) = method switch
            {
                "OrderChanged" => ($"Order {notice.GetProperty("orderNumber").GetString()}", $"Status: {notice.GetProperty("status").GetString()}", $"/admin/orders/{notice.GetProperty("orderNumber").GetString()}"),
                "StockAlert" => ("Low stock", $"A product has {notice.GetProperty("stock").GetInt32()} units remaining. Review inventory before accepting more orders.", "/admin/catalog"),
                _ => (notice.GetProperty("title").GetString()!, notice.GetProperty("body").GetString()!, notice.GetProperty("href").GetString()),
            };
            admins = await db.Users.Where(u => u.Role == "admin").Select(u => u.Id).ToListAsync(ct);
            db.Alerts.AddRange(admins.Select(id => new Alert { UserId = id, Kind = "operations", Title = title, Body = body, Href = href }));
        }
        await jobs.EnqueueAsync(db, new RealtimePush(method, JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)), group, userId), ct: ct);
        if (admins is { Count: > 0 })
            await NotificationsCache.InvalidateAsync(cache, admins, ct);
    }
}

/// <summary>
/// Cross-replica "one alert per crossing" dedupe. Redis SET NX is the gate when replicas are
/// enabled; a process-local dictionary keeps tests and single-process development honest.
/// </summary>
public sealed class StockAlerts
{
    static readonly TimeSpan GateTtl = TimeSpan.FromDays(7);
    readonly ConcurrentDictionary<(Guid ProductId, int Threshold), bool> dipping = new();
    readonly IConnectionMultiplexer? redis;
    readonly string prefix;
    readonly ILogger<StockAlerts> log;

    public StockAlerts(IConfiguration config, ILogger<StockAlerts> log, IConnectionMultiplexer? redis = null)
    {
        this.redis = redis;
        this.log = log;
        prefix = config["Replicas:Namespace"] ?? "TechLandierDistributed";
    }

    /// <summary>True exactly once each time stock falls below the threshold; resets on restock.</summary>
    public async ValueTask<bool> ShouldAlertAsync(Guid productId, int stock, int threshold, CancellationToken ct = default)
    {
        if (stock >= threshold)
        {
            dipping.TryRemove((productId, threshold), out _);
            if (redis is not null)
            {
                try { await redis.GetDatabase().KeyDeleteAsync(Key(productId, threshold)); }
                catch (Exception ex) when (IsRedisFailure(ex)) { log.LogWarning(ex, "Could not clear the stock alert gate for {ProductId}", productId); }
            }
            return false;
        }

        if (redis is null) return dipping.TryAdd((productId, threshold), true);
        try
        {
            return await redis.GetDatabase().StringSetAsync(Key(productId, threshold), "1", GateTtl, When.NotExists);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            // Never suppress an alert because the gate store is down; fall back to this process.
            log.LogWarning(ex, "Stock alert gate unavailable; falling back to in-memory dedupe for {ProductId}", productId);
            return dipping.TryAdd((productId, threshold), true);
        }
    }

    string Key(Guid productId, int threshold) => $"{prefix}:stock-alert:{productId}:{threshold}";

    static bool IsRedisFailure(Exception ex) => ex is RedisException or TimeoutException or ObjectDisposedException;
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
