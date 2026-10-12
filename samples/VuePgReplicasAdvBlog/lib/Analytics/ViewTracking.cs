using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Features.Analytics;

public sealed record ViewVisit(Guid? UserId, string? IpAddress, string UserAgent, string? Referrer, string AppHost);

public sealed record RecordView(Guid Id, Guid PostId, string ViewerHash, string? Referrer, DateTime At) : IJob;

/// Filters and hashes the beacon, then persists it for an independent worker. Analytics failures do not fail the beacon.
public sealed partial class ViewTracker(IConfiguration config, AppDb db, JobQueue jobs, ILogger<ViewTracker> log)
{
    // ponytail: constant fallback salt; production must set App:AnalyticsSalt (hashes are only as private as the salt is secret).
    readonly string _salt = config["App:AnalyticsSalt"] is { Length: > 0 } s ? s : "inkwell-dev-salt";

    [GeneratedRegex("bot|crawl|spider|slurp|preview|headless", RegexOptions.IgnoreCase)]
    private static partial Regex BotRegex();

    public async Task TrackAsync(ViewVisit visit, Guid postId, Guid authorId, CancellationToken ct = default)
    {
        try
        {
            var userId = visit.UserId;
            if (userId == authorId) return;

            var ua = visit.UserAgent;
            if (string.IsNullOrWhiteSpace(ua) || BotRegex().IsMatch(ua)) return;
            var now = DateTime.UtcNow;
            var viewerKey = userId is { } id ? $"u:{id}" : $"a:{visit.IpAddress}|{ua}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{viewerKey}|{now:yyyy-MM-dd}|{_salt}")))[..32].ToLowerInvariant();

            // The beacon's own Referer is always this app, so the client sends the real navigation referrer in the body.
            string? host = null;
            if (Uri.TryCreate(visit.Referrer, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https"
                && !uri.Host.Equals(visit.AppHost, StringComparison.OrdinalIgnoreCase))
                host = uri.Host.ToLowerInvariant();

            await jobs.EnqueueAsync(db, new RecordView(Guid.CreateVersion7(), postId, hash, host, now), ct: ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not queue a view for post {PostId}", postId);
        }
    }
}
