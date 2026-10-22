using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Bookings;
using CarParker.Infrastructure;

namespace CarParker.Features.Insurance;

public enum PolicyStatus { Active, Expired, Void }
public enum ClaimKind { Damage, Theft, BrokenWindow, Other }
public enum ClaimStatus { Submitted, UnderReview, Approved, Paid, Rejected }

/// <summary>"Spot Cover": one policy per reservation, priced at booking.</summary>
public sealed class SpotCoverPolicy
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ReservationId { get; set; }
    public Guid UserId { get; set; }
    public Guid LotId { get; set; }
    public long PremiumCents { get; set; }
    public long CoverageCents { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
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

public sealed record CustomerPolicyRow(
    Guid Id, long PremiumCents, long CoverageCents, string Status, DateTime StartedAt, DateTime CreatedAt,
    Guid ReservationId, string? LotName, DateTime? ReservedThrough);

public sealed record CustomerClaimRow(
    Guid Id, Guid PolicyId, string Kind, string Description, long ClaimedCents, long? PayoutCents,
    string Status, DateTime CreatedAt, DateTime? PaidAt, string? DecisionNote);

public sealed record CustomerInsurancePage(CustomerPolicyRow[] Policies, CustomerClaimRow[] Claims);

public sealed record AdminPolicyRow(
    Guid Id, Guid ReservationId, long PremiumCents, long CoverageCents, DateTime StartedAt, string Status,
    string? LotName, string? UserName, string? UserEmail);

public sealed record AdminClaimRow(
    Guid Id, Guid PolicyId, string Description, long ClaimedCents, long? PayoutCents, DateTime IncidentOn,
    string? DecisionNote, string Kind, string Status, string? UserName, DateTime CreatedAt);

public sealed record AdminInsurancePage(AdminPolicyRow[] Policies, AdminClaimRow[] Claims);
