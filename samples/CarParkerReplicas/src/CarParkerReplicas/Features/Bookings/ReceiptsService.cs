using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;

namespace CarParker.Features.Bookings;

/// <summary>Loads the data behind the receipt PDFs. Authorization is DetailAsync's: 404 for a missing booking, 403 for someone else's.</summary>
public sealed class ReceiptsService(AppDb db, BookingsService bookings)
{
    public async Task<(BookingReceipt? Receipt, Outcome? Failure)> BookingAsync(Guid id, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var (detail, failure) = await bookings.DetailAsync(id, userId, isAdmin, ct);
        if (detail is null) return (null, failure);
        var view = detail.View;
        var ownerId = await db.Reservations.Find(r => r.Id == id).Project(r => r.UserId).FirstAsync(ct);
        var owner = await db.Users.Find(u => u.Id == ownerId).Project(u => new { u.Name, u.Email }).FirstOrDefaultAsync(ct);
        var lot = await db.Lots.Find(l => l.Id == view.LotId).Project(l => new { l.Address, l.ContactPhone }).FirstOrDefaultAsync(ct);
        return (new BookingReceipt(
            view.Id, owner?.Name ?? "Customer", owner?.Email ?? "", view.LotName, lot?.Address ?? "", lot?.ContactPhone, view.SpotLabel,
            view.WindowType, view.StartAtUtc, view.EndAtUtc, view.OccurrenceCount, view.PlateNo,
            view.ParkingCents, view.InsuranceCents, view.TotalCents, view.Status, view.CreatedAt,
            detail.Payment?.Method, detail.Payment?.Status, detail.Payment?.PaidAt, detail.Payment?.Id, view.LotTimeZoneId), null);
    }

    /// <summary>The refund receipt; 404 when the booking has no refund.</summary>
    public async Task<(RefundReceipt? Receipt, Outcome? Failure)> RefundAsync(Guid id, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var (receipt, failure) = await BookingAsync(id, userId, isAdmin, ct);
        if (receipt is null) return (null, failure);
        var refund = await db.Refunds.Find(r => r.ReservationId == id).SortByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
        if (refund is null) return (null, Outcome.Missing());
        return (new RefundReceipt(receipt, refund.Id, refund.Rule, refund.Reason, refund.AmountCents,
            refund.Status.ToString().ToLowerInvariant(), refund.CreatedAt, refund.PaidAt), null);
    }
}
