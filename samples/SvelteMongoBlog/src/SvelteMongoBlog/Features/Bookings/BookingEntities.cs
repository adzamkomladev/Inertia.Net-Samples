using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using SvelteMongoBlog.Features.Lots;

namespace SvelteMongoBlog.Features.Bookings;

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

/// <summary>Cancellation refund matrix (pure): ≥24h before start → full, ≥4h → 50%, <4h or started → none.</summary>
public static class RefundRules
{
    public const string Full = "24h_full";
    public const string Half = "4h_half";
    public const string None = "late_none";
    public const string PolicyPayout = "policy_payout";

    public static (string Rule, long Cents) ComputeCents(DateTime? startAtUtc, long totalCents, DateTime now)
    {
        if (startAtUtc is not { } start) return (None, 0);
        var lead = start - now;
        if (lead >= TimeSpan.FromHours(24)) return (Full, totalCents);
        if (lead >= TimeSpan.FromHours(4)) return (Half, totalCents / 2);
        return (None, 0);
    }
}

/// <summary>Pricing helpers: all money is cents-long (`RateCents`/`TotalCents`); "$" is applied on the client only.</summary>
public static class PricingRules
{
    public const int InsurancePremiumMinCents = 200;
    public const int InsurancePercent = 12;
    public const int MaxRecurringWeeks = 8;

    /// <summary>ceil($rate × hours, min 1h) capped by DailyMax per 24h slice from the window start.
    /// Whole minutes are priced, sub-minute tails are dropped — the customer never pays three hours
    /// for a 2h+30µs window.</summary>
    public static long WindowCents(long ratePerHourCents, long dailyMaxCents, DateTime startUtc, DateTime endUtc)
    {
        if (startUtc.Kind is not DateTimeKind.Utc) startUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        if (endUtc.Kind is not DateTimeKind.Utc) endUtc = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc);
        var minutes = Math.Floor(Math.Max(0, (endUtc - startUtc).TotalSeconds) / 60.0);
        var hours = Math.Max(1, (long)Math.Ceiling(minutes / 60.0));
        long total = 0;
        while (hours > 0)
        {
            var slice = Math.Min(24, hours);
            var sliceCents = slice * ratePerHourCents;
            if (sliceCents > dailyMaxCents && dailyMaxCents > 0) sliceCents = dailyMaxCents;
            total += sliceCents;
            hours -= slice;
        }
        return total;
    }

    /// <summary>12% of the booking, minimum $2.</summary>
    public static long InsurancePremiumCents(long bookingCents)
    {
        var premium = bookingCents * InsurancePercent / 100;
        return Math.Max(InsurancePremiumMinCents, premium);
    }
}
