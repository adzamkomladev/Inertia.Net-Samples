using System.ComponentModel.DataAnnotations;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Features.Comments;

public sealed class CommentInput
{
    [Required, StringLength(2000, MinimumLength = 1)]
    public string? Body { get; set; }
}

public sealed record CommentItem(
    Guid Id,
    string Body,
    DateTime CreatedAt,
    AuthorDto Author,
    bool CanDelete,
    bool Liked = false,
    int LikeCount = 0
)
{
    public string Cursor => CursorHelpers.Cursor(CreatedAt, Id);
}

public sealed record CommentPage(List<CommentItem> Data, string? NextCursor);
