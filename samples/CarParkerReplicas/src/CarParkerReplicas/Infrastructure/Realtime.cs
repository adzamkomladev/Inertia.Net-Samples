using System.Text.Json;
using CarParker.Data;
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

/// <summary>SpotChanged payload broadcast to everyone watching a lot.</summary>
public sealed record SpotChangeEvent(Guid LotId, Guid SpotId, string State);

/// <summary>Persists pushes; the job worker handles delivery and retries.</summary>
public sealed class RealtimeEvents(IServiceScopeFactory scopes, JobQueue jobs)
{
    // shortcut: live delivery is process-local; add a SignalR backplane when running multiple instances.
    public async Task SpotChangedAsync(Guid lotId, Guid spotId, string state, CancellationToken ct = default)
    {
        await SendAsync(null, null, "SpotChanged", new SpotChangeEvent(lotId, spotId, state), ct);
        await LotChangedAsync(lotId, ct);
    }

    public async Task LotChangedAsync(Guid lotId, CancellationToken ct = default) => await SendAsync(null, null, "LotPulse", new { lotId }, ct);

    /// <summary>kind: session | refund | incident | claim</summary>
    public async Task BoardChangedAsync(string kind, CancellationToken ct = default) =>
        await SendAsync(ParkHub.StaffGroup, null, "BoardChanged", new { kind }, ct);

    public async Task NotifyUserAsync(Guid userId, string message, string? href = null, CancellationToken ct = default) =>
        await SendAsync(null, userId.ToString(), "Notification", new { message, href }, ct);

    public async Task NotifyStaffAsync(string message, string? href = null, CancellationToken ct = default) =>
        await SendAsync(ParkHub.StaffGroup, null, "Notification", new { message, href }, ct);

    async Task SendAsync(string? group, string? userId, string method, object payload, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await jobs.EnqueueAsync(db, new RealtimePush(method, JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)), group, userId), ct: ct);
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
