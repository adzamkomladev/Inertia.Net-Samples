using Microsoft.AspNetCore.Identity;

namespace Inkwell.Data;

public sealed class PostLike
{
    public Guid PostId { get; set; }
    public required Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>One row per post, visitor and UTC day: deduplicated daily views.</summary>
public sealed class PostView
{
    public Guid PostId { get; set; }
    public required string VisitorHash { get; set; }
    public DateOnly Day { get; set; }
}
