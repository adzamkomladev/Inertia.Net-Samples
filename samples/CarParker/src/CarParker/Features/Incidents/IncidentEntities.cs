using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CarParker.Features.Incidents;

/// <summary>Guard-reported security incident: unauthorized entry, theft, damage or a simple patrol note.</summary>
public sealed class SecurityIncident
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid LotId { get; set; }
    public Guid ReporterUserId { get; set; }
    /// <summary>unauthorized_entry | theft | damage | patrol_note | other.</summary>
    public required string Kind { get; set; }
    [BsonIgnoreIfNull] public string? PlateNo { get; set; }
    public required string Description { get; set; }
    /// <summary>open | resolved.</summary>
    [BsonRepresentation(BsonType.String)] public IncidentStatus Status { get; set; } = IncidentStatus.Open;
    [BsonIgnoreIfNull] public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonIgnoreIfNull] public DateTime? ResolvedAt { get; set; }
}

public enum IncidentStatus { Open, Resolved }
