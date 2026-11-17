using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TechLandier.Data;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Runwick;
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

/// <summary>
/// The event and channel names both sides speak: MainApp declares the live props with them and the
/// worker publishes with them, so the two tiers cannot drift apart. Plain strings on purpose - Core
/// must not reference Inertia.Net, so the apps build the events from these constants.
/// </summary>
public static class RealtimeNames
{
    /// <summary>The per-user notification badge and alerts page.</summary>
    public const string NotificationEvent = "Notification";
    /// <summary>The admin dashboards and boards on the admins channel.</summary>
    public const string AdminPulseEvent = "AdminPulse";
    /// <summary>The tracking/order page timelines, published on the order's own public channel.</summary>
    public const string TimelineEvent = "OrderTimelineUpdated";

    public const string UserChannelPrefix = "users.";
    public const string AdminChannel = "admins";
    public const string OrderChannelPrefix = "orders.";
}

/// One server-to-client push: a SignalR method plus who receives it (a user, the admins group or everyone).
public sealed record RealtimeMessage(string? Group, string? UserId, string Method, object Payload);

/// <summary>
/// Produces realtime work as Runwick jobs on the events queue. It never touches a hub directly, so the web
/// role can run without SignalR and every replica can dispatch for the worker.
/// </summary>
public sealed class RealtimeEvents(IServiceScopeFactory scopes, IJobOutbox jobs, IPropCacheInvalidator cache)
{
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static RealtimeMessage OrderChanged(TechLandier.Features.Order order) =>
        new("admins", null, "OrderChanged", new OrderPulse(order.Number, order.Status, order.PaymentStatus));

    public static RealtimeMessage Notification(Guid userId, string title, string body, string? href) =>
        new(null, userId.ToString(), "Notification", new { title, body, href });

    public static RealtimeMessage OrderTimeline(Guid userId, string orderNumber) =>
        new(null, userId.ToString(), "OrderTimelineUpdated", new { orderNumber });

    /// Admin dashboards refresh their KPI strip whenever any order changes shape.
    public async Task OrderChangedAsync(TechLandier.Features.Order order, CancellationToken ct = default) => await PushAsync([OrderChanged(order)], ct);

    /// Per-user inbox push (Alert rows are the durable copy).
    public async Task NotifyUserAsync(Guid userId, string title, string body, string? href, CancellationToken ct = default) =>
        await PushAsync([Notification(userId, title, body, href)], ct);

    /// Nudges a customer's open tracking/order pages to reload the timeline.
    public async Task OrderTimelineUpdatedAsync(Guid userId, string orderNumber, CancellationToken ct = default) =>
        await PushAsync([OrderTimeline(userId, orderNumber)], ct);

    public async Task StockAlertAsync(Guid productId, int stock, CancellationToken ct = default) =>
        await PushAsync([new("admins", null, "StockAlert", new { productId, stock })], ct);

    /// Generic admin busy-light (SLA breaches, escalation pulses).
    public async Task AdminPulseAsync(string title, string body, string? href, CancellationToken ct = default) =>
        await PushAsync([new("admins", null, "Notification", new { title, body, href })], ct);

    /// Sends any number of pushes with one dispatch. Pushes to users need no write and are dispatched directly; admin
    /// notices also leave an Alert row per admin, committed in one transaction with the staged push jobs.
    public async Task PushAsync(IReadOnlyList<RealtimeMessage> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0) return;
        var pushes = messages.Select(m => (IQueueJob)new RealtimePush(m.Method, JsonSerializer.SerializeToElement(m.Payload, Web), m.Group, m.UserId)).ToList();
        var adminNotices = messages.Where(m => m.Group == "admins" && m.Method is "Notification" or "OrderChanged" or "StockAlert").ToList();
        if (adminNotices.Count == 0)
        {
            await jobs.DispatchManyAsync(pushes, ct);
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var admins = await db.Users.AsNoTracking().Where(u => u.Role == "admin").Select(u => u.Id).ToListAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var m in adminNotices)
        {
            var notice = JsonSerializer.SerializeToElement(m.Payload, Web);
            var (title, body, href) = m.Method switch
            {
                "OrderChanged" => ($"Order {notice.GetProperty("orderNumber").GetString()}", $"Status: {notice.GetProperty("status").GetString()}", $"/admin/orders/{notice.GetProperty("orderNumber").GetString()}"),
                "StockAlert" => ("Low stock", $"A product has {notice.GetProperty("stock").GetInt32()} units remaining. Review inventory before accepting more orders.", "/admin/catalog"),
                _ => (notice.GetProperty("title").GetString()!, notice.GetProperty("body").GetString()!, notice.GetProperty("href").GetString()),
            };
            db.Alerts.AddRange(admins.Select(id => new Alert { UserId = id, Kind = "operations", Title = title, Body = body, Href = href }));
        }
        await db.SaveChangesAsync(ct);
        await jobs.StageManyAsync(db, pushes, ct);
        await tx.CommitAsync(ct);
        if (admins.Count > 0)
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
        // Both SignalR hubs authenticate with the session cookie and sit outside antiforgery validation: the app's own
        // /realtime and the Inertia live-props hub, which was previously unchecked.
        if (context.Request.Path.StartsWithSegments("/realtime") || context.Request.Path.StartsWithSegments("/inertia-live"))
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
