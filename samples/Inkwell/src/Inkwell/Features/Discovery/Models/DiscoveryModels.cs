namespace Inkwell.Features.Discovery;

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

public sealed record RefreshInterestProfile(Guid UserId);

public enum DiscoverySignalKind
{
    Like,
    Comment,
    View,
}

public sealed record DiscoverySignal(
    string UserId,
    string PostId,
    DiscoverySignalKind Kind,
    DateTimeOffset OccurredAtUtc
);

public sealed record DiscoveryPost(
    string Id,
    string AuthorId,
    IReadOnlyCollection<string> TopicIds,
    DateTimeOffset CreatedAtUtc,
    bool IsArchived = false
);

public sealed record DiscoveryProfile(
    IReadOnlyDictionary<string, double> TopicWeights,
    IReadOnlyDictionary<string, double> AuthorWeights
)
{
    public bool HasSignals => TopicWeights.Count > 0 || AuthorWeights.Count > 0;
}

public sealed record ScoredDiscoveryPost(DiscoveryPost Post, double Score);

internal sealed record ProfileVersion(DateTime UpdatedAt, bool Dirty);
