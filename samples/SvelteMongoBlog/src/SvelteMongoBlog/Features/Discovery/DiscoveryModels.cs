using SvelteMongoBlog.Infrastructure;

namespace SvelteMongoBlog.Features.Discovery;

public enum DiscoveryFeedMode
{
    ForYou,
    FollowingInterests,
}

public sealed record DiscoveryItem(Guid PostId, double Score, string Reason);

public sealed record DiscoveryPage(
    IReadOnlyList<DiscoveryItem> Items,
    string? NextCursor,
    bool HasMore
);

public sealed record RefreshInterestProfile(Guid UserId) : IJob;

public enum DiscoverySignalKind
{
    Like,
    Comment,
    View,
}

/// <summary>A single user/post/action signal. Repeated signals of the same kind are capped per post.</summary>
public sealed record DiscoverySignal(
    string UserId,
    string PostId,
    DiscoverySignalKind Kind,
    DateTimeOffset OccurredAtUtc
);

/// <summary>The fields needed to score a post, using string IDs so the scorer is provider-neutral.</summary>
public sealed record DiscoveryPost(
    string Id,
    string AuthorId,
    IReadOnlyCollection<string> TopicIds,
    DateTimeOffset CreatedAtUtc,
    bool IsArchived = false
);

/// <summary>Separate normalized topic and author vectors, each capped at twenty entries.</summary>
public sealed record DiscoveryProfile(
    IReadOnlyDictionary<string, double> TopicWeights,
    IReadOnlyDictionary<string, double> AuthorWeights
)
{
    public bool HasSignals => TopicWeights.Count > 0 || AuthorWeights.Count > 0;
}

public sealed record ScoredDiscoveryPost(DiscoveryPost Post, double Score);

public sealed partial class DiscoveryService
{
    sealed record ProfileRevision(Guid UserId, long Revision, DateTime UpdatedAt, bool Dirty);

    sealed record SnapshotMetadata(
        string Id,
        Guid UserId,
        string Mode,
        string ProfileSignature,
        DateTime CreatedAt,
        DateTime ExpiresAt
    );

    sealed record FeedProfile(DiscoveryProfile Profile, Guid[] FolloweeIds);

    sealed record CandidateRow(Guid Id, Guid AuthorId, DateTime PublishedAt, DateTime? ArchivedAt);
}
