using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VuePgReplicasAdvBlog.Data;

namespace VuePgReplicasAdvBlog.Infrastructure;

// No client methods: connections receive persisted updates only.
public sealed class BlogHub : Hub;
public sealed record PostCounters(Guid Id, long Sequence, int LikeCount, int CommentCount, int ViewCount, int RepostCount);

public sealed class RealtimeEvents(IServiceScopeFactory scopes, IHubContext<BlogHub> hub, ILogger<RealtimeEvents> logger)
{
    // shortcut: serialize snapshot reads and publication across replicas; use per-post persisted versions at higher event throughput.
    // Delivery remains transient; a durable inbox is needed for offline notifications.
    const long PublicationLock = 742109318;

    public async Task ChangedAsync(Guid postId, string? actor = null, string? notification = null, bool published = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ct = timeout.Token;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({PublicationLock})", ct);
            var post = await db.Posts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(p => p.Id == postId, ct);
            if (post is null) return;
            var owner = post.AuthorId.ToString();
            if (post.ArchivedAt is null)
            {
                var sequence = await db.Database.SqlQueryRaw<long>("SELECT nextval('\"RealtimeSequence\"') AS \"Value\"").SingleAsync(ct);
                var counters = new PostCounters(postId, sequence, post.LikeCount, post.CommentCount,
                    await db.PostViews.CountAsync(v => v.PostId == postId, ct), await db.Reposts.CountAsync(r => r.PostId == postId, ct));
                await hub.Clients.All.SendAsync("PostChanged", counters, ct);
                if (published) await hub.Clients.All.SendAsync("NewPost", postId, ct);
                if (notification is not null && actor != owner)
                    await hub.Clients.User(owner).SendAsync("Notification", new { message = notification, href = "/posts/" + post.Slug }, ct);
            }
            await hub.Clients.User(owner).SendAsync("AnalyticsChanged", postId, ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime delivery failed for post {PostId}", postId); }
    }

    public async Task RemovedAsync(Guid postId, string owner, bool wasPublic)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ct = timeout.Token;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({PublicationLock})", ct);
            if (wasPublic) await hub.Clients.All.SendAsync("PostRemoved", postId, ct);
            await hub.Clients.User(owner).SendAsync("AnalyticsChanged", postId, ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Realtime removal delivery failed for post {PostId}", postId); }
    }
}
