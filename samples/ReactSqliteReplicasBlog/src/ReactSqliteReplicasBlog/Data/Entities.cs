using Microsoft.AspNetCore.Identity;

namespace ReactSqliteReplicasBlog.Data;
// All timestamps are UTC DateTime: the SQLite provider cannot order or compare DateTimeOffset.

public sealed class AppUser : IdentityUser<Guid>
{
    public AppUser() => Id = Guid.CreateVersion7();

    public bool IsDemo { get; set; }
    public string? DemoKey { get; set; }
    public DateTime? NextDigestAtUtc { get; set; }
    public string DisplayName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public Preferences Preferences { get; set; } = new();
}

/// <summary>Owned by <see cref="AppUser"/>, stored as columns on AspNetUsers.</summary>
public sealed class Preferences
{
    public bool DailyRecommendationDigest { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";
    public bool EmailOnComment { get; set; } = true;
    public bool EmailOnLike { get; set; }
    public bool WeeklyDigest { get; set; } = true;
}

public sealed class Post
{
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ArchivedAt { get; set; }
    public bool IsPublished { get; set; } = true;
    public string? DemoKey { get; set; }
    public long IndexVersion { get; set; } = 1;
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

public enum JobStatus
{
    Pending = 0,
    Running = 1,
    Done = 2,
    Failed = 3,
}

public sealed class Job
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>The payload type name, e.g. "SendVerificationEmail".</summary>
    public required string Type { get; set; }
    public required string Payload { get; set; }
    public JobStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime RunAt { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
