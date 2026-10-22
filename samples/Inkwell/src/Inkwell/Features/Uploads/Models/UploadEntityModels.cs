using Microsoft.AspNetCore.Identity;

namespace Inkwell.Data;

public sealed class Upload
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required Guid OwnerId { get; set; }
    /// <summary>Object key in the media bucket, e.g. uploads/2026/10/{guid}.png.</summary>
    public required string Key { get; set; }
    public required string Url { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    /// <summary>Set when a post references it. Unattached uploads older than 24 h are deleted by cron.</summary>
    public Guid? PostId { get; set; }
    /// <summary>True when used as the owner's avatar.</summary>
    public bool IsAvatar { get; set; }
    public DateTime CreatedAt { get; set; }
}
