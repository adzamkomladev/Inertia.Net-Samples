using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Alerts;
using CarParker.Features.Bookings;

namespace CarParker.Infrastructure;

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
        var refund = await db.Refunds.Find(x => x.Id == m.RefundId).FirstOrDefaultAsync(ct);
        if (refund is null) return;
        var user = await db.Users.Find(x => x.Id == refund.RequestedByUserId).FirstOrDefaultAsync(ct);
        if (user is null || !user.Preferences.EmailOnRefund) return;
        var email = Emails.RefundPaid(user.Email, user.Name, refund.AmountCents, refund.ReservationId.ToString(), "/account/payments");
        await mailer.SendAsync(email.To, email.Subject, email.Html, email.Text, ct);
    }
}

/// <summary>Sent per occurrence by the reservation sweeper; the alert row it leaves marks the occurrence as swept.</summary>
public sealed class ReservationReminderEmailHandler(AppDb db, Mailer mailer, AlertsService alerts) : IJobHandler<ReservationReminderEmail>
{
    public async Task HandleAsync(ReservationReminderEmail m, CancellationToken ct)
    {
        var reservation = await db.Reservations.Find(x => x.Id == m.ReservationId).FirstOrDefaultAsync(ct);
        var occurrence = await db.Occurrences.Find(x => x.Id == m.OccurrenceId).FirstOrDefaultAsync(ct);
        if (reservation is not { Status: ReservationStatus.Confirmed } || occurrence is not { Status: OccurrenceStatus.Scheduled }) return;
        var user = await db.Users.Find(x => x.Id == reservation.UserId).FirstOrDefaultAsync(ct);
        if (user is null || !user.Preferences.BookingReminders) return;
        var lot = await db.Lots.Find(x => x.Id == reservation.LotId).Project(x => x.Name).FirstOrDefaultAsync(ct);
        if (lot is null) return;
        var spot = await db.Spots.Find(x => x.Id == reservation.SpotId).Project(x => x.Label).FirstOrDefaultAsync(ct) ?? "your spot";
        var href = $"/account/bookings/{reservation.Id}";
        var email = Emails.ReservationReminder(user.Email, user.Name, lot, spot, occurrence.StartAtUtc, href);
        await mailer.SendAsync(email.To, email.Subject, email.Html, email.Text, ct);
        await alerts.CreateAsync(user.Id, "reservation_soon", "Parking reminder", $"occurrence:{occurrence.Id}", href, ct: ct);
    }
}

/// <summary>Claim decision email, sent when an admin pays or rejects a claim.</summary>
public sealed class ClaimDecisionEmailHandler(AppDb db, Mailer mailer) : IJobHandler<ClaimDecisionEmail>
{
    public async Task HandleAsync(ClaimDecisionEmail m, CancellationToken ct)
    {
        var claim = await db.PolicyClaims.Find(x => x.Id == m.ClaimId).FirstOrDefaultAsync(ct);
        if (claim is null) return;
        var user = await db.Users.Find(x => x.Id == claim.UserId).FirstOrDefaultAsync(ct);
        if (user is null) return;
        var paid = string.Equals(m.Decision, "paid", StringComparison.OrdinalIgnoreCase);
        var email = Emails.ClaimDecision(user.Email, user.Name, claim.Kind.ToString().ToLowerInvariant(),
            paid, paid ? claim.PayoutCents : null, claim.DecisionNote ?? "", "/account/insurance");
        await mailer.SendAsync(email.To, email.Subject, email.Html, email.Text, ct);
    }
}
