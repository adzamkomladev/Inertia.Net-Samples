using Microsoft.EntityFrameworkCore;

namespace Inkwell.Data;

public sealed class PostTopic
{
    public Guid PostId { get; set; }
    public required string Topic { get; set; }
}

public sealed class Follow
{
    public Guid FollowerId { get; set; }
    public Guid FolloweeId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Bookmark
{
    public Guid UserId { get; set; }
    public Guid PostId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class CommentLike
{
    public Guid UserId { get; set; }
    public Guid CommentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Repost
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public Guid PostId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class SeenPost
{
    public Guid UserId { get; set; }
    public Guid PostId { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}

public sealed class InterestProfile
{
    public Guid UserId { get; set; }
    public string TopicsJson { get; set; } = "{}";
    public string AuthorsJson { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; }
    public bool Dirty { get; set; } = true;
}

public sealed class FeedSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Guid UserId { get; set; }
    public required string Mode { get; set; }
    public string ItemsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(30);
}

public sealed class DigestDelivery
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public DateOnly LocalDate { get; set; }
    public required string TimeZoneId { get; set; }
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string PostIdsJson { get; set; } = "[]";
    public string Status { get; set; } = "Queued";
    public string? ProviderReference { get; set; }
    public string? LastError { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Retained when a post is deleted so the external search document can be removed.
public sealed class SearchIndexState
{
    public Guid PostId { get; set; }
    public long RequestedVersion { get; set; }
    public long AppliedVersion { get; set; }
    public long? TaskId { get; set; }
    public long? TaskVersion { get; set; }
    public string? LeaseToken { get; set; }
    public DateTime? LockedUntil { get; set; }
}

public sealed class SeedCheckpoint
{
    public required string Name { get; set; }
    public int Completed { get; set; }
    public int Seed { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class SocialModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<PostTopic>(e =>
        {
            e.HasKey(x => new { x.PostId, x.Topic });
            e.Property(x => x.Topic).HasMaxLength(32);
            e.HasIndex(x => new { x.Topic, x.PostId });
            e.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Follow>(e =>
        {
            e.HasKey(x => new { x.FollowerId, x.FolloweeId });
            e.HasIndex(x => new { x.FolloweeId, x.FollowerId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.FollowerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.FolloweeId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_Follow_NotSelf", "\"FollowerId\" <> \"FolloweeId\""));
        });
        b.Entity<Bookmark>(e =>
        {
            e.HasKey(x => new { x.UserId, x.PostId });
            e.HasIndex(x => new { x.UserId, x.CreatedAt, x.PostId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CommentLike>(e =>
        {
            e.HasKey(x => new { x.CommentId, x.UserId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Comment>().WithMany().HasForeignKey(x => x.CommentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Repost>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.PostId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CreatedAt, x.Id });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SeenPost>(e =>
        {
            e.HasKey(x => new { x.UserId, x.PostId });
            e.HasIndex(x => new { x.UserId, x.LastSeenAt });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<InterestProfile>(e =>
        {
            e.HasKey(x => x.UserId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Dirty, x.UpdatedAt });
        });
        b.Entity<FeedSnapshot>(e =>
        {
            e.HasIndex(x => x.ExpiresAt);
            e.HasIndex(x => new { x.UserId, x.Mode, x.CreatedAt });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<DigestDelivery>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.LocalDate }).IsUnique();
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SearchIndexState>(e =>
        {
            e.HasKey(x => x.PostId);
            e.HasIndex(x => x.LockedUntil);
            e.HasIndex(x => x.PostId).HasFilter("\"RequestedVersion\" > \"AppliedVersion\""); // pending reindex rows only
        });
        b.Entity<SeedCheckpoint>().HasKey(x => x.Name);
    }
}
