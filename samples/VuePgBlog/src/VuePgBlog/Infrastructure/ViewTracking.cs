using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;

namespace VuePgBlog.Infrastructure;

/// Body of the view beacon: the original navigation referrer (document.referrer).
public sealed record ViewBeacon(string? Referrer);

internal sealed record ViewEvent(Guid PostId, string ViewerHash, string? Referrer, DateTime At);

/// Request-path side: filters and hashes, then drops the event into a bounded in-memory channel. Never throws, never blocks.
public sealed partial class ViewTracker(IConfiguration config)
{
    // ponytail: constant fallback salt; production must set App:AnalyticsSalt (hashes are only as private as the salt is secret).
    readonly string _salt = config["App:AnalyticsSalt"] is { Length: > 0 } s ? s : "inkwell-dev-salt";

    readonly Channel<ViewEvent> _channel = Channel.CreateBounded<ViewEvent>(new BoundedChannelOptions(10_000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    internal ChannelReader<ViewEvent> Reader => _channel.Reader;

    [GeneratedRegex("bot|crawl|spider|slurp|preview|headless", RegexOptions.IgnoreCase)]
    private static partial Regex BotRegex();

    public void Track(HttpContext ctx, Guid postId, Guid authorId, string? referrer = null)
    {
        try
        {
            var req = ctx.Request;
            // Called from the POST /posts/{id}/view beacon; partial reloads are never a new view.
            if (req.Headers.ContainsKey("X-Inertia-Partial-Component")) return;
            var userId = ctx.User.UserIdOrNull();
            if (userId == authorId) return;

            var ua = req.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(ua) || BotRegex().IsMatch(ua)) return;
            if (req.Headers["Purpose"].ToString().Equals("prefetch", StringComparison.OrdinalIgnoreCase)
                || req.Headers["Sec-Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase)) return;

            var now = DateTime.UtcNow;
            var viewerKey = userId is { } id ? $"u:{id}" : $"a:{ctx.Connection.RemoteIpAddress}|{ua}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{viewerKey}|{now:yyyy-MM-dd}|{_salt}")))[..32].ToLowerInvariant();

            // The beacon's own Referer is always this app, so the client sends the real navigation referrer in the body.
            string? host = null;
            if (Uri.TryCreate(referrer ?? req.Headers.Referer.ToString(), UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https"
                && !uri.Host.Equals(req.Host.Host, StringComparison.OrdinalIgnoreCase))
                host = uri.Host.ToLowerInvariant();

            _channel.Writer.TryWrite(new ViewEvent(postId, hash, host, now));
        }
        catch
        {
            // Analytics must never break a page view.
        }
    }
}

/// Drains the channel in batches (500 events or 2s) and bulk-inserts them; flushes what is buffered on shutdown.
public sealed class ViewEventWriter(ViewTracker tracker, IServiceScopeFactory scopes, ILogger<ViewEventWriter> log, RealtimeEvents realtime) : BackgroundService
{
    const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = tracker.Reader;
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                var batch = new List<ViewEvent>(BatchSize);
                using var window = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                window.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    while (batch.Count < BatchSize && await reader.WaitToReadAsync(window.Token))
                        while (batch.Count < BatchSize && reader.TryRead(out var e)) batch.Add(e);
                }
                catch (OperationCanceledException) { }
                await Flush(batch);
            }
        }
        catch (OperationCanceledException) { }

        // Shutdown: write whatever is still buffered.
        var rest = new List<ViewEvent>();
        while (reader.TryRead(out var e))
        {
            rest.Add(e);
            if (rest.Count == BatchSize) { await Flush(rest); rest = []; }
        }
        await Flush(rest);
    }

    static List<PostView> ToRows(IEnumerable<ViewEvent> events) =>
        events.Select(e => new PostView { PostId = e.PostId, ViewerHash = e.ViewerHash, Referrer = e.Referrer, At = e.At }).ToList();

    async Task Flush(List<ViewEvent> batch)
    {
        if (batch.Count == 0) return;
        try
        {
            await Insert(ToRows(batch));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Inserting {Count} views failed; retrying only rows whose post still exists", batch.Count);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                var ids = batch.Select(r => r.PostId).Distinct().ToList();
                var alive = (await db.Posts.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync()).ToHashSet();
                await Insert(ToRows(batch.Where(r => alive.Contains(r.PostId))));
            }
            catch (Exception ex2)
            {
                log.LogError(ex2, "Dropping {Count} view events", batch.Count);
            }
        }
    }

    async Task Insert(List<PostView> rows)
    {
        if (rows.Count == 0) return;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        db.PostViews.AddRange(rows);
        await db.SaveChangesAsync(CancellationToken.None);
        foreach (var postId in rows.Select(r => r.PostId).Distinct())
            await realtime.ChangedAsync(postId);
    }
}
