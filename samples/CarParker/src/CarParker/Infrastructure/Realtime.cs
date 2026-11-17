using System.Text.Json;
using CarParker.Data;
using CarParker.Features.Alerts;
using Inertia.Net;
using Microsoft.AspNetCore.SignalR;

namespace CarParker.Infrastructure;

// No client-callable methods: connections receive persisted updates only.
// SignalR groups come from role claims: admin/guard connections join "staff" for board pushes.
public sealed class ParkHub : Hub
{
    public const string StaffGroup = "staff";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user?.IsInRole("admin") == true || user?.IsInRole("guard") == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, StaffGroup);
        await base.OnConnectedAsync();
    }
}

/// <summary>
/// Board events on the private staff channel: guard consoles and admin boards reload the props that
/// listen for them when one console changes shared state. The channel authorizes by role claims.
/// </summary>
public static class StaffEvents
{
    public static LiveEvent Board() => new("BoardChanged", LiveChannel.Private(ParkHub.StaffGroup));
}

/// <summary>SpotChanged payload broadcast to everyone watching a lot.</summary>
public sealed record SpotChangeEvent(Guid LotId, Guid SpotId, string State);

/// <summary>Hands pushes to Runwick; the realtime-push job handler delivers them with retries. Delivery is best effort: a
/// failed dispatch is logged and never fails the request whose write already committed.</summary>
public sealed class RealtimeEvents(JobEnqueuer jobs, ILogger<RealtimeEvents> log)
{
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // shortcut: live delivery is process-local; add a SignalR backplane when running multiple instances.
    public async Task SpotChangedAsync(Guid lotId, Guid spotId, string state, CancellationToken ct = default) =>
        await SendAsync([Push(null, null, "SpotChanged", new SpotChangeEvent(lotId, spotId, state)), Push(null, null, "LotPulse", new { lotId })], ct);

    public async Task LotChangedAsync(Guid lotId, CancellationToken ct = default) => await SendAsync([Push(null, null, "LotPulse", new { lotId })], ct);

    /// <summary>kind: session | refund | incident | claim</summary>
    public async Task BoardChangedAsync(string kind, CancellationToken ct = default) =>
        await SendAsync([Push(ParkHub.StaffGroup, null, "BoardChanged", new { kind })], ct);

    public async Task NotifyUserAsync(Guid userId, string message, string? href = null, CancellationToken ct = default) =>
        await SendAsync([Push(null, userId.ToString(), "Notification", new { message, href })], ct);

    /// <summary>One batched dispatch for many recipients (cron sweeps).</summary>
    public async Task NotifyUsersAsync(IEnumerable<(Guid UserId, string Message, string? Href)> notices, CancellationToken ct = default) =>
        await SendAsync(notices.Select(n => Push(null, n.UserId.ToString(), "Notification", new { message = n.Message, href = n.Href })).ToList(), ct);

    public async Task NotifyStaffAsync(string message, string? href = null, CancellationToken ct = default) =>
        await SendAsync([Push(ParkHub.StaffGroup, null, "Notification", new { message, href })], ct);

    static RealtimePush Push(string? group, string? userId, string method, object payload) =>
        new(method, JsonSerializer.SerializeToElement(payload, Web), group, userId);

    async Task SendAsync(IReadOnlyList<RealtimePush> pushes, CancellationToken ct)
    {
        if (pushes.Count == 0) return;
        try { await jobs.DispatchManyAsync(pushes, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Could not dispatch {Count} realtime push job(s)", pushes.Count);
        }
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
