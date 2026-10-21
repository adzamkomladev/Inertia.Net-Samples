using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SvelteMongoBlog.Features.Insurance;

public enum PolicyStatus { Active, Expired, Void }
public enum ClaimKind { Damage, Theft, BrokenWindow, Other }
public enum ClaimStatus { Submitted, UnderReview, Approved, Paid, Rejected }

/// <summary>"Spot Cover" security/insurance policy: one per reservation, priced at booking.</summary>
public sealed class SpotCoverPolicy
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ReservationId { get; set; }
    public Guid UserId { get; set; }
    public Guid LotId { get; set; }
    public long PremiumCents { get; set; }
    public long CoverageCents { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Cover ends with the reservation's last occurrence; null while unknown/basic single-day cover.</summary>
    [BsonIgnoreIfNull] public DateTime? EndedAt { get; set; }
    [BsonRepresentation(BsonType.String)] public PolicyStatus Status { get; set; } = PolicyStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class PolicyClaim
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PolicyId { get; set; }
    public Guid UserId { get; set; }
    [BsonRepresentation(BsonType.String)] public ClaimKind Kind { get; set; } = ClaimKind.Other;
    public required string Description { get; set; }
    public DateTime IncidentOn { get; set; }
    public long ClaimedCents { get; set; }
    [BsonIgnoreIfNull] public long? PayoutCents { get; set; }
    [BsonRepresentation(BsonType.String)] public ClaimStatus Status { get; set; } = ClaimStatus.Submitted;
    [BsonIgnoreIfNull] public string? DecisionNote { get; set; }
    [BsonIgnoreIfNull] public Guid? DecidedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonIgnoreIfNull] public DateTime? DecidedAt { get; set; }
    [BsonIgnoreIfNull] public DateTime? PaidAt { get; set; }
}
