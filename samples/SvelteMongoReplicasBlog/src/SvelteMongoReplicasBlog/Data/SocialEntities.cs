using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SvelteMongoReplicasBlog.Data;

public sealed class PostTopic
{
    public Guid PostId { get; set; }
    public required string Topic { get; set; }
    [BsonId] public string Id { get => $"{PostId}:{Topic}"; set { var p = value.Split(':', 2); PostId = Guid.Parse(p[0]); Topic = p[1]; } }
}
public sealed class Follow
{
    public Guid FollowerId { get; set; }
    public Guid FolloweeId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonId] public string Id { get => $"{FollowerId}:{FolloweeId}"; set { var p = value.Split(':'); FollowerId = Guid.Parse(p[0]); FolloweeId = Guid.Parse(p[1]); } }
}
public sealed class Bookmark
{
    public Guid UserId { get; set; }
    public Guid PostId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonId] public string Id { get => $"{UserId}:{PostId}"; set { var p = value.Split(':'); UserId = Guid.Parse(p[0]); PostId = Guid.Parse(p[1]); } }
}
public sealed class CommentLike
{
    public Guid UserId { get; set; }
    public Guid CommentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonId] public string Id { get => $"{CommentId}:{UserId}"; set { var p = value.Split(':'); CommentId = Guid.Parse(p[0]); UserId = Guid.Parse(p[1]); } }
}
public sealed class Repost
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
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
    [BsonId] public string Id { get => $"{UserId}:{PostId}"; set { var p = value.Split(':'); UserId = Guid.Parse(p[0]); PostId = Guid.Parse(p[1]); } }
}
public sealed class InterestProfile
{
    [BsonId] public Guid UserId { get; set; }
    public string TopicsJson { get; set; } = "{}";
    public string AuthorsJson { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; }
    public bool Dirty { get; set; } = true;
    public long Revision { get; set; }
}
public sealed class FeedSnapshot
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Guid UserId { get; set; }
    public required string Mode { get; set; }
    public string ProfileSignature { get; set; } = "";
    public string ItemsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(30);
}
public sealed class DigestDelivery
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    [BsonRepresentation(BsonType.String)] public DateOnly LocalDate { get; set; }
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
public sealed class SearchIndexState
{
    [BsonId] public Guid PostId { get; set; }
    public long RequestedVersion { get; set; }
    public long AppliedVersion { get; set; }
    public long? TaskId { get; set; }
    public long? TaskVersion { get; set; }
    public string? LeaseToken { get; set; }
    public DateTime? LockedUntil { get; set; }
}
public sealed class SeedCheckpoint
{
    [BsonId] public required string Name { get; set; }
    public int Completed { get; set; }
    public int Seed { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
