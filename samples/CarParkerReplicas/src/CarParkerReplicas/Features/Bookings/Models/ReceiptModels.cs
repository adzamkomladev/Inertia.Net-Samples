using CarParker.Infrastructure;

namespace CarParker.Features.Bookings;

/// <summary>Everything the booking receipt PDF prints. Cents throughout; times are UTC.</summary>
public sealed record BookingReceipt(
    Guid ReservationId, string CustomerName, string CustomerEmail,
    string LotName, string LotAddress, string? LotPhone, string SpotLabel,
    string WindowType, DateTime? StartAtUtc, DateTime? EndAtUtc, int OccurrenceCount, string? PlateNo,
    long ParkingCents, long InsuranceCents, long TotalCents, string Status, DateTime BookedAt,
    string? PaymentMethod, string? PaymentStatus, DateTime? PaidAt, Guid? PaymentId)
{
    public string Number => "RCP-" + Display.ShortId(ReservationId).ToUpperInvariant();
}

/// <summary>Everything the refund receipt PDF prints.</summary>
public sealed record RefundReceipt(
    BookingReceipt Booking, Guid RefundId, string Rule, string Reason, long AmountCents, string Status, DateTime RequestedAt, DateTime? PaidAt)
{
    public string Number => "RFD-" + Display.ShortId(RefundId).ToUpperInvariant();
}
