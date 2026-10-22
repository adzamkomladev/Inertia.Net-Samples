using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using CarParker.Features.Lots;

namespace CarParker.Features.Bookings;

public enum WindowType { Single, Recurring }
public enum ReservationStatus { Confirmed, Cancelled, Completed }
public enum OccurrenceStatus { Scheduled, Active, Done, Cancelled, NoShow }
public enum PaymentMethod { Card, MobileMoney, PayAtGate }
public enum PaymentStatus { Pending, Paid, Failed, Refunded }

/// <summary>Recurring weekly windows: a fixed time-of-day on the listed days of week, for N weeks (materialized).</summary>
public sealed record RecurrencePattern(int[] DaysOfWeek, string TimeFrom, string TimeTo, int Weeks);

public sealed class Reservation
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public Guid LotId { get; set; }
    public Guid SpotId { get; set; }
    [BsonRepresentation(BsonType.String)] public WindowType WindowType { get; set; } = WindowType.Single;
    [BsonIgnoreIfNull] public DateTime? StartAtUtc { get; set; }
    [BsonIgnoreIfNull] public DateTime? EndAtUtc { get; set; }
    [BsonIgnoreIfNull] public RecurrencePattern? Pattern { get; set; }
    public int OccurrenceCount { get; set; }
    [BsonIgnoreIfNull] public string? PlateNo { get; set; }
    /// <summary>Client-generated id used by the wizard retry path to find an already-created reservation.</summary>
    [BsonIgnoreIfNull] public string? AttemptId { get; set; }
    [BsonIgnoreIfNull] public Guid? InsuranceId { get; set; }
    /// <summary>Booked price including the insurance premium when billed at booking.</summary>
    public long TotalCents { get; set; }
    public long InsuranceCents { get; set; }
    public long ParkingCents { get; set; }
    [BsonRepresentation(BsonType.String)] public ReservationStatus Status { get; set; } = ReservationStatus.Confirmed;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonIgnoreIfNull] public DateTime? CancelledAt { get; set; }
    [BsonIgnoreIfNull] public DateTime? CompletedAt { get; set; }
    [BsonIgnoreIfNull] public string? Note { get; set; }
}

public sealed class Occurrence
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ReservationId { get; set; }
    public int Index { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    [BsonRepresentation(BsonType.String)] public OccurrenceStatus Status { get; set; } = OccurrenceStatus.Scheduled;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Payment
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ReservationId { get; set; }
    public Guid UserId { get; set; }
    [BsonRepresentation(BsonType.String)] public PaymentMethod Method { get; set; } = PaymentMethod.Card;
    public long AmountCents { get; set; }
    [BsonRepresentation(BsonType.String)] public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    [BsonIgnoreIfNull] public string? TxRef { get; set; }
    [BsonIgnoreIfNull] public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Refund
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ReservationId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid RequestedByUserId { get; set; }
    /// <summary>"24h_full" | "4h_half" | "late_none" | "policy_payout".</summary>
    public required string Rule { get; set; }
    /// <summary>Estimated by rule at request time, fixed on approval.</summary>
    public long AmountCents { get; set; }
    /// <summary>requested | approved | paid | rejected.</summary>
    [BsonRepresentation(BsonType.String)] public RefundStatus Status { get; set; } = RefundStatus.Requested;
    public required string Reason { get; set; }
    [BsonIgnoreIfNull] public string? DecisionNote { get; set; }
    [BsonIgnoreIfNull] public Guid? ApprovedByUserId { get; set; }
    [BsonIgnoreIfNull] public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum RefundStatus { Requested, Approved, Paid, Rejected }
