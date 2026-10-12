using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ReactSqliteReplicasBlog.Data;

namespace ReactSqliteReplicasBlog.Infrastructure;

// No client methods: connections receive persisted updates only.
public sealed class BlogHub : Hub;
public sealed record PostCounters(Guid Id, long Sequence, int LikeCount, int CommentCount, int ViewCount, int RepostCount);

public sealed class RealtimeEvents(IServiceScopeFactory scopes, IHubContext<BlogHub> hub, ILogger<RealtimeEvents> logger)
{
    // Redis broadcasts to all hosts. Clients refetch persisted data; Sequence is publisher-local
    // diagnostics only and must never order updates from independent replicas.
    // shortcut: delivery is transient; reconnect prompts a refresh rather than replaying offline events.
    // ponytail: serialize snapshots globally; use per-post gates if event throughput becomes a bottleneck.
    readonly SemaphoreSlim gate = new(1, 1);
    long sequence;

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
                    post.ViewCount, await db.Reposts.CountAsync(r => r.PostId == postId, ct));
                await hub.Clients.All.SendAsync("PostChanged", counters, ct);
                if (published) await hub.Clients.All.SendAsync("NewPost", postId, ct);
                if (notification is not null && actor != owner)
                    await hub.Clients.User(owner.ToString()).SendAsync("Notification", new { message = notification, href = $"/posts/{post.Id}" }, ct);
            }
            await hub.Clients.User(owner.ToString()).SendAsync("AnalyticsChanged", postId, ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime delivery failed for post {PostId}", postId); }
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
            await hub.Clients.User(owner.ToString()).SendAsync("AnalyticsChanged", postId, ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime removal delivery failed for post {PostId}", postId); }
        finally { if (entered) gate.Release(); }
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
