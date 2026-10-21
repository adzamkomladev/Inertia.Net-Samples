using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SvelteMongoBlog.Features.Guard;

public enum SessionStatus { InProgress, Exited }

/// <summary>A guard-logged parking session. Guard actions set EnteredBy/ExitedBy; overstay ends set CancelReason.</summary>
public sealed class ParkingSession
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OccurrenceId { get; set; }
    public Guid SpotId { get; set; }
    public Guid LotId { get; set; }
    public required string PlateNo { get; set; }
    public DateTime EnteredAtUtc { get; set; } = DateTime.UtcNow;
    [BsonIgnoreIfNull] public DateTime? ExitedAtUtc { get; set; }
    [BsonIgnoreIfNull] public Guid? EnteredById { get; set; }
    [BsonIgnoreIfNull] public Guid? ExitedById { get; set; }
    /// <summary>Set when a guard manually ends the session (overstay resolve / force close).</summary>
    [BsonIgnoreIfNull] public string? CancelReason { get; set; }
    [BsonRepresentation(BsonType.String)] public SessionStatus Status { get; set; } = SessionStatus.InProgress;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
