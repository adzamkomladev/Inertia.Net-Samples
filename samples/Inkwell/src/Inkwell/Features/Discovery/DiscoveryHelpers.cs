using System.Globalization;
using System.Text.Json;
using Inkwell.Data;

namespace Inkwell.Features.Discovery;

/// <summary>
/// Deterministic, bounded recommendation math. Querying and persisting candidate snapshots belong to
/// the provider-specific feature layer; this class only turns sparse signals into stable scores.
/// </summary>
public static class RecommendationScoring
{
    public static readonly TimeSpan ProfileWindow = TimeSpan.FromDays(90);
    public static readonly TimeSpan DecayHalfLife = TimeSpan.FromDays(14);
    public const int MaximumTopics = 20;
    public const int MaximumAuthors = 20;
    public const double TopicWeight = 0.70;
    public const double AuthorWeight = 0.30;
    public const double MinimumAffinity = 0.05;
    static readonly DiscoveryProfile EmptyProfile = new(
        new Dictionary<string, double>(StringComparer.Ordinal),
        new Dictionary<string, double>(StringComparer.Ordinal)
    );

    /// <summary>Builds one user's profile from unique post/action signals in the trailing 90 days.</summary>
    public static DiscoveryProfile BuildProfile(
        string userId,
        IEnumerable<DiscoverySignal> signals,
        IReadOnlyDictionary<string, DiscoveryPost> postsById,
        DateTimeOffset nowUtc
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(postsById);
        var now = nowUtc.ToUniversalTime();
        var earliest = now - ProfileWindow;
        var uniqueSignals = signals
            .Where(signal => string.Equals(signal.UserId, userId, StringComparison.Ordinal))
            .Where(signal =>
            {
                var occurredAt = signal.OccurredAtUtc.ToUniversalTime();
                return occurredAt >= earliest && occurredAt <= now;
            }) // A repeated like/comment/view for the same post contributes at most once. For views,
            // use the latest occurrence so its decay reflects the latest observed interest.
            .GroupBy(signal => (signal.PostId, signal.Kind))
            .Select(group => group.MaxBy(signal => signal.OccurredAtUtc)!)
            .OrderBy(signal => signal.PostId, StringComparer.Ordinal)
            .ThenBy(signal => signal.Kind)
            .ToArray();
        var topicTotals = new Dictionary<string, double>(StringComparer.Ordinal);
        var authorTotals = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var signal in uniqueSignals)
        {
            if (!postsById.TryGetValue(signal.PostId, out var post))
                continue;
            var actionWeight = signal.Kind switch
            {
                DiscoverySignalKind.Like => 4d,
                DiscoverySignalKind.Comment => 2d,
                DiscoverySignalKind.View => 1d,
                _ => 0d,
            };
            if (actionWeight <= 0)
                continue;
            var age = now - signal.OccurredAtUtc.ToUniversalTime();
            var decayedWeight =
                actionWeight * Math.Pow(0.5d, age.TotalDays / DecayHalfLife.TotalDays);
            if (!string.IsNullOrWhiteSpace(post.AuthorId))
                Add(authorTotals, post.AuthorId, decayedWeight);
            var topics = post
                .TopicIds.Where(topic => !string.IsNullOrWhiteSpace(topic))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (topics.Length == 0)
                continue;
            var weightPerTopic = decayedWeight / topics.Length;
            foreach (var topic in topics)
                Add(topicTotals, topic, weightPerTopic);
        }

        return new DiscoveryProfile(
            NormalizeTop(topicTotals, MaximumTopics),
            NormalizeTop(authorTotals, MaximumAuthors)
        );
    }

    /// <summary>
    /// Averages equally weighted, individually normalized followee vectors. High-volume followees
    /// therefore do not overwhelm lower-volume followees.
    /// </summary>
    public static DiscoveryProfile MeanNormalizedProfiles(IEnumerable<DiscoveryProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var normalized = profiles
            .Select(NormalizeProfile)
            .Where(profile => profile.HasSignals)
            .ToArray();
        if (normalized.Length == 0)
            return EmptyProfile;
        return new DiscoveryProfile(
            MeanVector(normalized.Select(profile => profile.TopicWeights), MaximumTopics),
            MeanVector(normalized.Select(profile => profile.AuthorWeights), MaximumAuthors)
        );
    }

    /// <summary>Returns a post's topic/author match against one selected profile, before recency.</summary>
    public static double ProfileScore(DiscoveryPost post, DiscoveryProfile profile) =>
        Match(post, profile);

    /// <summary>
    /// Scores a candidate against exactly one profile. Callers select either the viewer's own
    /// profile (For you/digest) or the mean-normalized followed-user profile (Following interests).
    /// </summary>
    public static double Score(DiscoveryPost post, DiscoveryProfile profile, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(profile);
        var score = ProfileScore(post, profile) * RecencyFactor(post.CreatedAtUtc, nowUtc);
        return score < MinimumAffinity ? 0 : score;
    }

    /// <summary>Ranks a bounded candidate set with stable tie-breaks and excludes own/archived posts.</summary>
    public static IReadOnlyList<ScoredDiscoveryPost> Rank(
        IEnumerable<DiscoveryPost> candidates,
        string viewerId,
        DiscoveryProfile profile,
        DateTimeOffset nowUtc
    )
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewerId);
        return candidates
            .Where(post =>
                !post.IsArchived
                && !string.Equals(post.AuthorId, viewerId, StringComparison.Ordinal)
            )
            .Select(post => new ScoredDiscoveryPost(post, Score(post, profile, nowUtc)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Post.CreatedAtUtc)
            .ThenBy(item => item.Post.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Exponential recency factor with a 14-day half-life, bounded from 1.0 to 0.5.</summary>
    public static double RecencyFactor(DateTimeOffset createdAtUtc, DateTimeOffset nowUtc)
    {
        var ageDays = Math.Max(
            0,
            (nowUtc.ToUniversalTime() - createdAtUtc.ToUniversalTime()).TotalDays
        );
        return 0.5d + 0.5d * Math.Pow(0.5d, ageDays / DecayHalfLife.TotalDays);
    }

    static double Match(DiscoveryPost post, DiscoveryProfile profile)
    {
        if (!profile.HasSignals)
            return 0;
        var authorAffinity = profile.AuthorWeights.TryGetValue(post.AuthorId, out var authorWeight)
            ? authorWeight
            : 0;
        var distinctTopics = post
            .TopicIds.Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var topicAffinity = distinctTopics.Sum(topic =>
            profile.TopicWeights.TryGetValue(topic, out var weight) ? weight : 0
        );
        return TopicWeight * Math.Min(topicAffinity, 1d) + AuthorWeight * authorAffinity;
    }

    static DiscoveryProfile NormalizeProfile(DiscoveryProfile profile) =>
        new(
            NormalizeTop(profile.TopicWeights, MaximumTopics),
            NormalizeTop(profile.AuthorWeights, MaximumAuthors)
        );

    static IReadOnlyDictionary<string, double> MeanVector(
        IEnumerable<IReadOnlyDictionary<string, double>> vectors,
        int limit
    )
    {
        var nonEmpty = vectors.Where(vector => vector.Count > 0).ToArray();
        if (nonEmpty.Length == 0)
            return new Dictionary<string, double>(StringComparer.Ordinal);
        var totals = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var vector in nonEmpty)
        foreach (var (key, weight) in vector)
            Add(totals, key, weight / nonEmpty.Length);
        return NormalizeTop(totals, limit);
    }

    static Dictionary<string, double> NormalizeTop(
        IReadOnlyDictionary<string, double> totals,
        int limit
    )
    {
        var top = totals
            .Where(pair => pair.Value > 0 && double.IsFinite(pair.Value))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
        var sum = top.Sum(pair => pair.Value);
        if (sum <= 0)
            return new Dictionary<string, double>(StringComparer.Ordinal);
        return top.ToDictionary(pair => pair.Key, pair => pair.Value / sum, StringComparer.Ordinal);
    }

    static void Add(Dictionary<string, double> totals, string key, double value)
    {
        totals.TryGetValue(key, out var current);
        totals[key] = current + value;
    }
}

/// <summary>Builds stable ranked snapshots and keeps sparse interest profiles current.</summary>
public sealed partial class DiscoveryService
{
    const int ProfileBatchSize = 100;
    const int MaximumFeedCandidates = 2_000;
    const int MaximumSnapshotItems = 1_000;
    const int MaximumDigestCandidates = 2_000;
    static readonly TimeSpan SnapshotLifetime = TimeSpan.FromMinutes(30);
    static readonly TimeSpan ProfileCacheLifetime = TimeSpan.FromMinutes(5);
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    static FeedSnapshot ToSnapshot(SnapshotMetadata metadata) =>
        new()
        {
            Id = metadata.Id,
            UserId = metadata.UserId,
            Mode = metadata.Mode,
            CreatedAt = metadata.CreatedAt,
            ExpiresAt = metadata.ExpiresAt,
            ItemsJson = string.Empty,
        };

    static IEnumerable<DiscoveryItem> RankAndDescribe(
        IReadOnlyCollection<DiscoveryPost> candidates,
        Guid viewerId,
        DiscoveryProfile profile,
        DateTimeOffset now
    ) =>
        RecommendationScoring
            .Rank(candidates, viewerId.ToString(), profile, now)
            .Select(item => new DiscoveryItem(
                Guid.Parse(item.Post.Id),
                item.Score,
                Reason(item.Post, profile)
            ));

    static string Reason(DiscoveryPost post, DiscoveryProfile profile)
    {
        var topic = post.TopicIds.Any(profile.TopicWeights.ContainsKey);
        var author = profile.AuthorWeights.ContainsKey(post.AuthorId);
        if (topic && author)
            return "Matches your topics and authors";
        return topic ? "Matches your topics" : "Matches an author you engage with";
    }

    static bool TryDeserializeProfile(InterestProfile row, out DiscoveryProfile profile) =>
        TryDeserializeProfile(row.TopicsJson, row.AuthorsJson, out profile);

    static bool TryDeserializeProfile(
        string topicsJson,
        string authorsJson,
        out DiscoveryProfile profile
    )
    {
        try
        {
            profile = new DiscoveryProfile(
                JsonSerializer.Deserialize<Dictionary<string, double>>(topicsJson, JsonOptions)
                    ?? [],
                JsonSerializer.Deserialize<Dictionary<string, double>>(authorsJson, JsonOptions)
                    ?? []
            );
            return true;
        }
        catch (JsonException)
        {
            profile = EmptyProfile();
            return false;
        }
    }

    static DiscoverySignal Signal(
        Guid userId,
        Guid postId,
        DiscoverySignalKind kind,
        DateTime at
    ) =>
        new(
            userId.ToString(),
            postId.ToString(),
            kind,
            new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc))
        );

    static DiscoveryProfile EmptyProfile() =>
        new(
            new Dictionary<string, double>(StringComparer.Ordinal),
            new Dictionary<string, double>(StringComparer.Ordinal)
        );

    static string ProfileCacheKey(Guid userId, DateTime updatedAtUtc) =>
        $"interest-profile:{userId}:{DateTime.SpecifyKind(updatedAtUtc, DateTimeKind.Utc).Ticks}";

    static string ModeName(DiscoveryFeedMode mode) =>
        mode switch
        {
            DiscoveryFeedMode.ForYou => "for-you",
            DiscoveryFeedMode.FollowingInterests => "following",
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

    static IReadOnlyList<DiscoveryItem> DeserializeItems(string json) =>
        JsonSerializer.Deserialize<List<DiscoveryItem>>(json, JsonOptions)
        ?? throw new InvalidDataException("Feed snapshot has no item list.");

    static string MakeCursor(string id, int offset) =>
        $"{id}.{offset.ToString(CultureInfo.InvariantCulture)}";

    static (string SnapshotId, int Offset) ParseCursor(string value)
    {
        if (value.Length is < 3 or > 80)
            throw new ArgumentException("Invalid feed cursor.", nameof(value));
        var dot = value.LastIndexOf('.');
        if (
            dot <= 0
            || !Guid.TryParseExact(value[..dot], "N", out _)
            || !int.TryParse(
                value[(dot + 1)..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var offset
            )
            || offset < 1
        )
            throw new ArgumentException("Invalid feed cursor.", nameof(value));
        return (value[..dot], offset);
    }
}
