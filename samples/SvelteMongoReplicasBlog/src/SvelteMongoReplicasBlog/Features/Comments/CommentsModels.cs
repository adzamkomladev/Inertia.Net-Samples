using System.ComponentModel.DataAnnotations;

namespace SvelteMongoReplicasBlog.Features;

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
    public string Cursor => SocialHelpers.Cursor(CreatedAt, Id);
}
