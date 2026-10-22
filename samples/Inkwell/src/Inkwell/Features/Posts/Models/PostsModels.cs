using System.ComponentModel.DataAnnotations;
using Inkwell.Infrastructure;

namespace Inkwell.Features.Posts;

public sealed record AuthorDto(Guid Id, string Name, string? AvatarUrl);

public sealed record PostCardDto(
    Guid Id,
    string Title,
    string Excerpt,
    string? Snippet,
    string? CoverUrl,
    DateTime CreatedAt,
    AuthorDto Author,
    int LikeCount,
    int CommentCount,
    int ViewCount
)
{
    public string[] Topics { get; init; } = [];
    public bool Bookmarked { get; init; }
    public bool Reposted { get; init; }
    public bool Liked { get; init; }
    public int RepostCount { get; init; }
    public DateTime? ArchivedAt { get; init; }
    public bool CanArchive { get; init; }
    public AuthorDto? Reposter { get; init; }
    public string? Reason { get; init; }
}

public sealed record FeedPage(IReadOnlyList<PostCardDto> Data, int Page, bool HasMore)
    : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}

public sealed record PostDetailDto(
    Guid Id,
    string Title,
    string Body,
    string? CoverUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    AuthorDto Author,
    int LikeCount,
    int CommentCount,
    int ViewCount
)
{
    public bool IsPublished { get; init; } = true;
    public string[] Topics { get; init; } = [];
    public bool Bookmarked { get; init; }
    public bool Reposted { get; init; }
    public int RepostCount { get; init; }
    public DateTime? ArchivedAt { get; init; }
    public bool CanArchive { get; init; }
    public AuthorDto? Reposter { get; init; }
    public string? Reason { get; init; }
}

public sealed record CommentDto(
    Guid Id,
    string Body,
    DateTime CreatedAt,
    AuthorDto Author,
    bool CanDelete
)
{
    public bool Liked { get; init; }
    public int LikeCount { get; init; }
    public string Cursor => Paging.Encode(CreatedAt, Id);
}

public sealed record PostEditDto(Guid Id, string Title, string Body, string? CoverUrl)
{
    public string[] Topics { get; init; } = [];
    public bool IsPublished { get; init; } = true;
}

public sealed class PostInput
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = "";

    [Required, MaxLength(50_000)]
    public string Body { get; set; } = "";
    public string? CoverUrl { get; set; }
    public string[]? Topics { get; set; }
    public bool? IsPublished { get; set; }
}

public sealed class CommentInput
{
    [Required, MaxLength(5000)]
    public string Body { get; set; } = "";
}

public sealed record CardPage(List<PostCardDto> Items, bool HasMore, string? Next);

public sealed record PostPage(PostDetailDto Post, bool Liked);

public sealed record CommentPageData(List<CommentDto> Data, string? NextCursor);

public sealed record PostEditPage(PostEditDto? Post, bool Forbidden);

internal sealed class CardRow
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? CoverUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public AuthorDto Author { get; set; } = null!;
    public int LikeCount { get; set; }
    public int CommentCount { get; set; }
    public int ViewCount { get; set; }
    public DateTime? ArchivedAt { get; set; }
}

internal sealed class SearchHit
{
    public Guid Id { get; set; }
    public string Snippet { get; set; } = "";
}
