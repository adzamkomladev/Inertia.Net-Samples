using VuePgReplicasAdvBlog.Features.Posts;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Features.Social;

public sealed record UserSummary(
    Guid Id,
    string Name,
    string? AvatarUrl,
    bool Following,
    int FollowerCount,
    int FollowingCount,
    bool IsSelf
);

public sealed record UserTimeline(UserSummary User, CursorPage<PostSummary> Posts);
