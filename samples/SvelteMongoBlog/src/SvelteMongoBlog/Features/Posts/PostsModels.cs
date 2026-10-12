using System.ComponentModel.DataAnnotations;

namespace SvelteMongoBlog.Features;

public sealed class PostInput
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string? Title { get; set; }

    [Required, StringLength(50000, MinimumLength = 1)]
    public string? Body { get; set; }
    public Guid? CoverAssetId { get; set; }
    public string[]? Topics { get; set; }
}

public sealed record AuthorDto(Guid Id, string Name, string? AvatarUrl);

public sealed record PostSummary(
    Guid Id,
    string Slug,
    string Title,
    string Excerpt,
    string? Snippet,
    string? CoverUrl,
    AuthorDto Author,
    int LikeCount,
    int CommentCount,
    DateTime CreatedAt
)
{
    public string[] Topics { get; init; } = [];
    public bool Liked { get; init; }
    public bool Bookmarked { get; init; }
    public bool Reposted { get; init; }
    public int RepostCount { get; init; }
    public int ViewCount { get; init; }
    public DateTime? ArchivedAt { get; init; }
    public bool CanArchive { get; init; }
    public AuthorDto? Reposter { get; init; }
    public string? Reason { get; init; }
}

public sealed record PostDetail(
    Guid Id,
    string Slug,
    string Title,
    string Excerpt,
    string? Snippet,
    string? CoverUrl,
    AuthorDto Author,
    int LikeCount,
    int CommentCount,
    DateTime CreatedAt,
    string Body,
    Guid? CoverAssetId,
    DateTime UpdatedAt,
    bool Liked,
    bool CanEdit
)
{
    public string[] Topics { get; init; } = [];
    public bool Bookmarked { get; init; }
    public bool Reposted { get; init; }
    public int RepostCount { get; init; }
    public int ViewCount { get; init; }
    public DateTime? ArchivedAt { get; init; }
    public bool CanArchive { get; init; }
}

public sealed record FeedPage(List<PostSummary> Data, int Page, bool HasMore)
    : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}
