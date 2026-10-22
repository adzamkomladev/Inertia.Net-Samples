using Microsoft.AspNetCore.Identity;

namespace Inkwell.Data;

public sealed class Post
{
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ArchivedAt { get; set; }
    public bool IsPublished { get; set; } = true;
    public string? DemoKey { get; set; }
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required Guid AuthorId { get; set; }
    public AppUser Author { get; set; } = null!;
    public required string Title { get; set; }
    /// <summary>Markdown.</summary>
    public required string Body { get; set; }
    public string? CoverUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Denormalized counters, updated in the same SaveChanges as the row they count.
    public int ViewCount { get; set; }
    public int LikeCount { get; set; }
    public int CommentCount { get; set; }

    public List<Comment> Comments { get; set; } = [];
}

public sealed class Comment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PostId { get; set; }
    public Post Post { get; set; } = null!;
    public required Guid AuthorId { get; set; }
    public AppUser Author { get; set; } = null!;
    public required string Body { get; set; }
    public DateTime CreatedAt { get; set; }
}
