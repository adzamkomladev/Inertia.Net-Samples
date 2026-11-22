using Inkwell.Jobs;
using Inkwell.Jobs.Handlers;
using Inkwell.Features.Analytics;
using Inkwell.Features.Notifications;
using Inertia.Net;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Inkwell.Data;
using System.Collections.Concurrent;
using Runwick;

namespace Inkwell.Infrastructure;

// No client methods: connections receive persisted updates only.
public sealed class BlogHub : Hub;
public sealed record PostCounters(Guid Id, long Sequence, int LikeCount, int CommentCount, int ViewCount, int RepostCount);

/// <summary>Queues realtime fan-out after the change has committed; delivery runs on the Runwick "events" queue.</summary>
public sealed class RealtimeEvents(IJobOutbox jobs)
{
    public async Task ChangedAsync(Guid postId, Guid? actor = null, string? notification = null, bool published = false) =>
        await jobs.DispatchAsync(new PostChanged(postId, actor, notification, published));

    public async Task RemovedAsync(Guid postId, Guid owner, bool wasPublic) =>
        await jobs.DispatchAsync(new PostRemoved(postId, owner, wasPublic));
}

public sealed class RealtimeDelivery(IJobOutbox jobs, IServiceScopeFactory scopes, IHubContext<BlogHub> hub, IInertiaLive live, ILogger<RealtimeDelivery> logger)
{
    // ponytail: process-local transient delivery; add a backplane and durable inbox for multiple instances/offline notifications.
    // ponytail: serialize snapshots globally; use per-post gates if event throughput becomes a bottleneck.
    readonly SemaphoreSlim gate = new(1, 1);
    long sequence;

    /// <summary>At most one analytics reload per owner per window: the first event of a burst is sent at once,
    /// the rest fold into one trailing <see cref="AnalyticsReload"/> job (playbook: throttle "something changed,
    /// reload" broadcasts). A scheduled trailing job marks the window closed; if it never runs (its instance went
    /// away) the mark expires so the next burst schedules a fresh one. Process-local like the delivery itself.</summary>
    static readonly ConcurrentDictionary<Guid, long> LastAnalyticsSend = new();
    static readonly ConcurrentDictionary<Guid, long> TrailingScheduled = new();
    static readonly TimeSpan AnalyticsWindow = TimeSpan.FromSeconds(1);
    static readonly TimeSpan TrailingStale = TimeSpan.FromSeconds(10);

    public async Task ChangedAsync(Guid postId, Guid? actor = null, string? notification = null, bool published = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ct = timeout.Token;
        var entered = false;
        try
        {
            await gate.WaitAsync(ct);
            entered = true;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var post = await db.Posts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(p => p.Id == postId, ct);
            if (post is null) return;
            var owner = post.AuthorId;
            if (post.ArchivedAt is null && post.IsPublished)
            {
                var counters = new PostCounters(postId, ++sequence, post.LikeCount, post.CommentCount,
                    post.ViewCount, post.RepostCount);
                await hub.Clients.All.SendAsync("PostChanged", counters, ct);
                if (published) await hub.Clients.All.SendAsync("NewPost", postId, ct);
                if (notification is not null && actor != owner)
                {
                    // The raw event carries the toast wording; the live event refreshes the badge and open inbox.
                    await hub.Clients.User(owner.ToString()).SendAsync("Notification", new { message = notification, href = $"/posts/{post.Id}" }, ct);
                    await live.PublishAsync(NotificationEvents.Created(owner), cancellationToken: ct);
                }
            }
            // The owner's open dashboards reload their cached analytics props; publish points evict first.
            await PublishAnalyticsAsync(owner, ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime delivery failed for post {PostId}", postId); throw; }
        finally { if (entered) gate.Release(); }
    }

    public async Task RemovedAsync(Guid postId, Guid owner, bool wasPublic)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ct = timeout.Token;
        var entered = false;
        try
        {
            await gate.WaitAsync(ct);
            entered = true;
            if (wasPublic) await hub.Clients.All.SendAsync("PostRemoved", postId, ct);
            // A removal invalidates the dashboards too; the delete path removed the cache entries just before enqueuing.
            await PublishAnalyticsTrailingAsync(owner, ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime removal delivery failed for post {PostId}", postId); throw; }
        finally { if (entered) gate.Release(); }
    }

    /// <summary>Publishes the owner's analytics reload now when the window has passed, otherwise schedules one trailing publish.</summary>
    public async Task PublishAnalyticsAsync(Guid owner, CancellationToken ct)
    {
        var now = Environment.TickCount64;
        if (!LastAnalyticsSend.TryGetValue(owner, out var last) || now - last >= AnalyticsWindow.Ticks)
        {
            await PublishAnalyticsTrailingAsync(owner, ct);
            return;
        }
        // Inside the window: one trailing publish covers the whole burst. UniqueWhileActive folds dispatches while
        // the job is queued (a refusal keeps the mark); any other dispatch failure forgets it so the next event retries.
        if (!TrailingScheduled.TryGetValue(owner, out var scheduled) || now - scheduled >= TrailingStale.Ticks)
        {
            TrailingScheduled[owner] = now;
            try { await jobs.DispatchAsync(new AnalyticsReload(owner), ct); }
            catch (UniquenessConflictException) { }
            catch (RunwickException) { TrailingScheduled.TryRemove(owner, out _); }
        }
    }

    /// <summary>Publishes now, records the time (so the next burst within the window folds into a trailing job) and
    /// marks any scheduled trailing delivered.</summary>
    public async Task PublishAnalyticsTrailingAsync(Guid owner, CancellationToken ct)
    {
        LastAnalyticsSend[owner] = Environment.TickCount64;
        TrailingScheduled.TryRemove(owner, out _);
        await live.PublishAsync(AnalyticsEvents.Changed(owner), cancellationToken: ct);
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
