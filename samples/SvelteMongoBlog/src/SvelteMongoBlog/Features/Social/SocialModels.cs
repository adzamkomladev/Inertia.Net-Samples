namespace SvelteMongoBlog.Features;

public sealed record CursorPage<T>(List<T> Data, string? Cursor, string? NextCursor)
    : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => new("cursor", null, NextCursor, Cursor ?? "start");
}

public sealed record UserSummary(
    Guid Id,
    string Name,
    string? AvatarUrl,
    bool Following,
    int FollowerCount,
    int FollowingCount,
    bool IsSelf
);

public static partial class SocialService
{
    public readonly record struct Edge(DateTime At, Guid Id);
}
