using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CarParker.Features.Guard;

public enum SessionStatus { InProgress, Exited }


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

    [BsonIgnoreIfNull] public string? CancelReason { get; set; }
    [BsonRepresentation(BsonType.String)] public SessionStatus Status { get; set; } = SessionStatus.InProgress;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum IncidentStatus { Open, Resolved }

/// <summary>Guard-reported incident: unauthorized entry, theft, damage or a patrol note.</summary>
public sealed class SecurityIncident
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid LotId { get; set; }
    public Guid ReporterUserId { get; set; }
    public required string Kind { get; set; }
    [BsonIgnoreIfNull] public string? PlateNo { get; set; }
    public required string Description { get; set; }
    [BsonRepresentation(BsonType.String)] public IncidentStatus Status { get; set; } = IncidentStatus.Open;
    [BsonIgnoreIfNull] public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonIgnoreIfNull] public DateTime? ResolvedAt { get; set; }
}
