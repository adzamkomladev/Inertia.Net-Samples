using System.Text.Json.Serialization;
using CarParker.Features.Lots;
using System.ComponentModel.DataAnnotations;

namespace CarParker.Features.Bookings;

public sealed class BookingInput
{
    [Required] public Guid? LotId { get; set; }
    [Required] public Guid? SpotId { get; set; }
    /// <summary>"single" | "recurring".</summary>
    [Required] public string? WindowType { get; set; }
    public DateTime? StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
    /// <summary>Recurring: .NET day-of-week numbers (Sunday = 0), e.g. "1,3,5".</summary>
    [MaxLength(64)] public string? DaysOfWeek { get; set; }
    /// <summary>Recurring: "HH:mm" UTC start of each weekly window.</summary>
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")] public string? TimeFrom { get; set; }
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")] public string? TimeTo { get; set; }
    [Range(1, PricingRules.MaxRecurringWeeks)] public int Weeks { get; set; } = 1;
    [MaxLength(16), RegularExpression(@"^[A-Za-z0-9 \-]{2,16}$")] public string? PlateNo { get; set; }
    public bool Insurance { get; set; }
    /// <summary>"card" | "mobile_money" | "pay_at_gate".</summary>
    [Required] public string? Method { get; set; }
    /// <summary>Client-generated id; a retry with the same attempt returns the first booking it created.</summary>
    [MaxLength(64)] public string? AttemptId { get; set; }
    [MaxLength(280)] public string? Note { get; set; }
}

public sealed class PayInput
{
    [Required] public string? Method { get; set; }
}

public sealed class ClaimInput
{
    /// <summary>"damage" | "theft" | "broken_window" | "other".</summary>
    [Required] public string? Kind { get; set; }
    [Required, MaxLength(500)] public string? Description { get; set; }
    public DateTime? IncidentOn { get; set; }
    [Range(100, 10_000_000)] public long ClaimedCents { get; set; }
}

public sealed class RefundDecisionInput
{
    [Range(0, 10_000_000)] public long AmountCents { get; set; }
    [MaxLength(280)] public string? Note { get; set; }
}

public sealed class ClaimDecisionInput
{
    [Range(0, 10_000_000)] public long PayoutCents { get; set; }
    [MaxLength(280)] public string? Note { get; set; }
}

public sealed class IncidentInput
{
    [Required] public Guid? LotId { get; set; }
    [Required, MaxLength(40)] public string? Kind { get; set; }
    [MaxLength(16)] public string? PlateNo { get; set; }
    [Required, MaxLength(500)] public string? Description { get; set; }
    /// <summary>Guard note when resolving an open incident.</summary>
    [MaxLength(280)] public string? Note { get; set; }
}

public sealed class SpotOverrideInput
{
    [Required, MaxLength(20)] public string? Action { get; set; } // block | clear
    [MaxLength(280)] public string? Note { get; set; }
}

public sealed class CheckInOutInput
{
    [Required] public Guid? LotId { get; set; }
    [Required] public Guid? SpotId { get; set; }
    [MaxLength(16), RegularExpression(@"^[A-Za-z0-9 \-]{2,16}$")] public string? PlateNo { get; set; }
}

public sealed class ForceCloseInput
{
    [MaxLength(120)] public string? Note { get; set; }
}

public sealed class LotInput
{
    [Required, MaxLength(80)] public string? Name { get; set; }
    [Required, MaxLength(160)] public string? Address { get; set; }
    [Required, MaxLength(60)] public string? Area { get; set; }
    [Range(2, 4)] public int LevelCount { get; set; } = 2;
    [Range(50, 200_000)] public long RatePerHourCents { get; set; } = 400;
    [Range(0, 500_000)] public long DailyMaxCents { get; set; } = 2400;
    [MaxLength(24), RegularExpression(@"^[0-9+()\- ]{6,24}$")] public string? ContactPhone { get; set; }
    public bool IsActive { get; set; } = true;
    [Range(0, 5_000_000)] public long CoverageCents { get; set; } = 50_000;
}

public sealed class BookingException(string message) : Exception(message);

public sealed record BookingView(
    Guid Id, Guid LotId, Guid SpotId, string LotName, string SpotLabel, string WindowType,
    long TotalCents, long ParkingCents, long InsuranceCents, string Status, DateTime CreatedAt,
    DateTime? CancelledAt, DateTime? StartAtUtc, DateTime? EndAtUtc, RecurrencePattern? Pattern,
    int OccurrenceCount, string? PlateNo, string? PolicyStatus, OccurrenceView[] Occurrences, string? Note);

public sealed record OccurrenceView(Guid Id, int Index, DateTime StartAtUtc, DateTime EndAtUtc, string Status);

public sealed record BookingDetail(
    BookingView View, BookingPaymentRow? Payment, BookingRefundRow? Refund,
    BookingSessionRow[] Sessions, BookingPolicyRow? Policy);

public sealed record BookingPaymentRow(Guid Id, string Method, long AmountCents, string Status, DateTime? PaidAt);

public sealed record BookingRefundRow(Guid Id, string Rule, long AmountCents, string Status, DateTime CreatedAt);

public sealed record BookingSessionRow(Guid Id, Guid SpotId, string PlateNo, DateTime EnteredAtUtc, DateTime? ExitedAtUtc, string Status);

public sealed record BookingPolicyRow(Guid Id, long PremiumCents, long CoverageCents, string Status, BookingClaimRow[] Claims);

public sealed record BookingClaimRow(Guid Id, string Kind, long ClaimedCents, long? PayoutCents, string Status, DateTime CreatedAt, string? DecisionNote);

public sealed record AccountUpcomingRow(Guid Id, long TotalCents, string Window, DateTime? StartAtUtc, DateTime? EndAtUtc, int OccurrenceCount, string? LotName, string? SpotLabel);

public sealed record AccountNextVisitRow(Guid Id, DateTime StartAtUtc, DateTime EndAtUtc, string? SpotLabel, string? LotName, Guid BookingId);

public sealed record AccountSessionRow(Guid Id, DateTime EnteredAtUtc, string? LotName, string? SpotLabel);

public sealed record AccountData(
    int UpcomingCount, AccountNextVisitRow? NextVisit, AccountUpcomingRow[] Upcoming,
    AccountSessionRow? ActiveSession, long Spend30dCents, int UnreadAlerts, [property: JsonIgnore] List<LotOption> Lots);

public sealed record CustomerPaymentRow(
    Guid Id, Guid ReservationId, long AmountCents, string Method, string Status,
    DateTime? PaidAt, DateTime CreatedAt, string? LotName, string? SpotLabel);

public sealed record CustomerRefundRow(Guid Id, Guid ReservationId, string Rule, long AmountCents, string Status, DateTime CreatedAt);

public sealed record PaymentsPage(CustomerPaymentRow[] Payments, CustomerRefundRow[] Refunds);
