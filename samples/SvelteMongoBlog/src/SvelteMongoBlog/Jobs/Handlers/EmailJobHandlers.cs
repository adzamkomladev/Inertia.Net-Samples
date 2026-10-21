using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Features.Alerts;
using SvelteMongoBlog.Features.Bookings;
using SvelteMongoBlog.Features.Insurance;
using SvelteMongoBlog.Features.Lots;

namespace SvelteMongoBlog.Infrastructure;

public sealed class SendEmailHandler(Mailer mailer) : IJobHandler<SendEmail>
{
    public Task HandleAsync(SendEmail m, CancellationToken ct) => mailer.SendAsync(m.To, m.Subject, m.Html, m.Text, ct);
}

public sealed class DeleteAssetsHandler(Storage storage) : IJobHandler<DeleteAssets>
{
    public Task HandleAsync(DeleteAssets m, CancellationToken ct) => storage.DeleteAsync(m.Keys, ct);
}

/// <summary>Refund paid notification; respects the customer's emailOnRefund preference.</summary>
public sealed class RefundPaidEmailHandler(AppDb db, Mailer mailer) : IJobHandler<RefundPaidEmail>
{
    public async Task HandleAsync(RefundPaidEmail m, CancellationToken ct)
    {
        var refund = await JobPayables.RefundAsync(db, m.RefundId, ct);
        if (refund is null) return;
        var reservation = await JobPayables.ReservationAsync(db, refund.ReservationId, ct);
        var user = await db.Users.Find(x => x.Id == refund.RequestedByUserId).FirstOrDefaultAsync(ct);
        if (reservation is null || user is null || !user.Preferences.EmailOnRefund) return;
        var lot = await db.Lots.Find(x => x.Id == reservation.LotId).Project(x => x.Name).FirstOrDefaultAsync(ct);
        await mailer.SendAsync(user.Email,
            Emails.RefundPaid(user.Email, user.Name, refund.AmountCents, reservation.Id.ToString(), "/account/payments").Subject,
            RefundBody(user.Name, refund.AmountCents, lot ?? "your lot"),
            $"Hi {user.Name}, your refund of {refund.AmountCents}c has been paid.",
            ct);
    }

    static string RefundBody(string name, long cents, string lot) =>
        $"<p>Hi {System.Net.WebUtility.HtmlEncode(name)}, a refund of <b>${cents / 100.0:F2}</b> has been paid back to you for the booking at {System.Net.WebUtility.HtmlEncode(lot)}.</p>";
}

/// <summary>Reservation reminder: insert cron sends this per occurrence; respects bookingReminders + emailOnCheckout-style opt-outs.</summary>
public sealed class ReservationReminderEmailHandler(AppDb db, Mailer mailer, RealtimeEvents realtime) : IJobHandler<ReservationReminderEmail>
{
    public async Task HandleAsync(ReservationReminderEmail m, CancellationToken ct)
    {
        var reservation = await JobPayables.ReservationAsync(db, m.ReservationId, ct);
        var occurrence = await db.Occurrences.Find(x => x.Id == m.OccurrenceId).FirstOrDefaultAsync(ct);
        if (reservation is null || occurrence is null || reservation.Status != ReservationStatus.Confirmed
            || occurrence.Status != OccurrenceStatus.Scheduled)
            return;
        var user = await db.Users.Find(x => x.Id == reservation.UserId).FirstOrDefaultAsync(ct);
        if (user is null || !user.Preferences.BookingReminders) return;
        var lot = await db.Lots.Find(x => x.Id == reservation.LotId).FirstOrDefaultAsync(ct);
        if (lot is null) return;
        var spotLabel = (await db.Spots.Find(x => x.Id == reservation.SpotId).Project(x => x.Label).FirstOrDefaultAsync(ct)) ?? "your spot";
        var email = Emails.ReservationReminder(user.Email, user.Name, lot.Name, spotLabel,
            occurrence.StartAtUtc, $"/account/bookings/{reservation.Id}");
        await mailer.SendAsync(email.To, email.Subject, email.Html, email.Text, ct);
        await AlertsService.CreateAsync(db, realtime, user.Id, "reservation_soon",
            "Parking reminder",
            $"occurrence:{occurrence.Id}", $"/account/bookings/{reservation.Id}", ct: ct);
    }
}

/// <summary>Claim decision email (approved/rejected); sent by the admin pay/reject endpoints.</summary>
public sealed class ClaimDecisionEmailHandler(AppDb db, Mailer mailer) : IJobHandler<ClaimDecisionEmail>
{
    public async Task HandleAsync(ClaimDecisionEmail m, CancellationToken ct)
    {
        var claim = await JobPayables.ClaimAsync(db, m.ClaimId, ct);
        if (claim is null) return;
        var user = await db.Users.Find(x => x.Id == claim.UserId).FirstOrDefaultAsync(ct);
        if (user is null) return;
        var approved = string.Equals(m.Decision, "paid", StringComparison.OrdinalIgnoreCase);
        var email = Emails.ClaimDecision(user.Email, user.Name, claim.Kind.ToString().ToLowerInvariant(),
            approved, approved ? claim.PayoutCents : null, claim.DecisionNote ?? "", "/account/insurance");
        await mailer.SendAsync(email.To, email.Subject, email.Html, email.Text, ct);
    }
}
