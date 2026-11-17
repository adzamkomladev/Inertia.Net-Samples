using Microsoft.AspNetCore.DataProtection;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features;
using CarParker.Features.Alerts;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
using CarParker.Features.Lots;
using Runwick;

namespace CarParker.Infrastructure;

public sealed class DeleteAssetsHandler(Storage storage) : IJobHandler<DeleteAssets>
{
    public async Task HandleAsync(DeleteAssets m, CancellationToken ct) => await storage.DeleteAsync(m.Keys, ct);
}

/// <summary>The token is minted here, not at enqueue time: a retry after a long outage still mails a link valid for 24 hours.</summary>
public sealed class SendVerificationEmailHandler(AppDb db, Mailer mailer, IConfiguration config, IHostEnvironment env, IDataProtectionProvider dp) : IJobHandler<SendVerificationEmail>
{
    public async Task HandleAsync(SendVerificationEmail m, CancellationToken ct)
    {
        var user = await db.Users.Find(u => u.Id == m.UserId).FirstOrDefaultAsync(ct);
        if (user is null || user.EmailVerifiedAt is not null) return;
        var baseUrl = config.AppBaseUrl() ?? throw new InvalidOperationException("App:BaseUrl must be configured to mail verification links.");
        await mailer.SendAsync(Emails.Verify(EmailContext.From(config, env), user.Email, user.Name, AuthService.VerifyUrl(dp, baseUrl, user)), ct);
    }
}

/// <summary>The reset link is minted here, so a retry after an outage still mails a link good for 60 minutes. A user who
/// became a demo account or was deleted meanwhile gets nothing.</summary>
public sealed class SendPasswordResetEmailHandler(AppDb db, Mailer mailer, IConfiguration config, IHostEnvironment env, IDataProtectionProvider dp) : IJobHandler<SendPasswordResetEmail>
{
    public async Task HandleAsync(SendPasswordResetEmail m, CancellationToken ct)
    {
        var user = await db.Users.Find(u => u.Id == m.UserId).FirstOrDefaultAsync(ct);
        if (user is null || user.IsDemo) return;
        var c = EmailContext.From(config, env);
        await mailer.SendAsync(Emails.PasswordReset(c, user.Email, user.Name, AuthService.ResetUrl(dp, c.BaseUrl.TrimEnd('/'), user)), ct);
    }
}

/// <summary>Sent for each window the no-show sweep released. Skipped if the window is no longer a no-show (an admin fixed it).
/// Times read on the lot's clock; the refund shown is the one the sweep requested for that window.</summary>
public sealed class NoShowEmailHandler(AppDb db, Mailer mailer, IConfiguration config, IHostEnvironment env) : IJobHandler<NoShowEmail>
{
    public async Task HandleAsync(NoShowEmail m, CancellationToken ct)
    {
        var occurrence = await db.Occurrences.Find(o => o.Id == m.OccurrenceId).FirstOrDefaultAsync(ct);
        if (occurrence is not { Status: OccurrenceStatus.NoShow }) return;
        var reservation = await db.Reservations.Find(r => r.Id == occurrence.ReservationId).FirstOrDefaultAsync(ct);
        if (reservation is null) return;
        var user = await db.Users.Find(u => u.Id == reservation.UserId).FirstOrDefaultAsync(ct);
        if (user is null) return;
        var lot = await db.Lots.Find(l => l.Id == reservation.LotId).Project(l => new { l.Name, l.TimeZoneId }).FirstOrDefaultAsync(ct);
        if (lot is null) return;
        var spot = await db.Spots.Find(s => s.Id == reservation.SpotId).Project(s => s.Label).FirstOrDefaultAsync(ct) ?? "—";
        var reason = $"No-show on window starting {occurrence.StartAtUtc:u}";
        var refund = await db.Refunds.Find(r => r.ReservationId == reservation.Id && r.Reason == reason).Project(r => (long?)r.AmountCents).FirstOrDefaultAsync(ct);
        var c = EmailContext.From(config, env);
        await mailer.SendAsync(Emails.NoShow(c, user.Email, user.Name, lot.Name, spot, LotTime.Span(occurrence.StartAtUtc, occurrence.EndAtUtc, lot.TimeZoneId),
            GuardService.NoShowMinutes, refund, c.Url($"/account/bookings/{reservation.Id}")), ct);
    }
}

/// <summary>Sent once per session the overstay sweep flags. Skipped if the car has left by the time the job runs.</summary>
public sealed class OverstayEmailHandler(AppDb db, Mailer mailer, IConfiguration config, IHostEnvironment env) : IJobHandler<OverstayEmail>
{
    public async Task HandleAsync(OverstayEmail m, CancellationToken ct)
    {
        var session = await db.ParkingSessions.Find(s => s.Id == m.SessionId).Project(s => new { s.Status, s.OccurrenceId, s.LotId, s.SpotId }).FirstOrDefaultAsync(ct);
        if (session is not { Status: SessionStatus.InProgress }) return;
        var occurrence = await db.Occurrences.Find(o => o.Id == session.OccurrenceId).Project(o => new { o.ReservationId, o.EndAtUtc }).FirstOrDefaultAsync(ct);
        if (occurrence is null) return;
        var reservation = await db.Reservations.Find(r => r.Id == occurrence.ReservationId).Project(r => new { r.Id, r.UserId }).FirstOrDefaultAsync(ct);
        if (reservation is null) return;
        var user = await db.Users.Find(u => u.Id == reservation.UserId).FirstOrDefaultAsync(ct);
        if (user is null) return;
        var lot = await db.Lots.Find(l => l.Id == session.LotId).Project(l => new { l.Name, l.TimeZoneId, l.ContactPhone }).FirstOrDefaultAsync(ct);
        if (lot is null) return;
        var spot = await db.Spots.Find(s => s.Id == session.SpotId).Project(s => s.Label).FirstOrDefaultAsync(ct) ?? "—";
        var c = EmailContext.From(config, env);
        await mailer.SendAsync(Emails.Overstay(c, user.Email, user.Name, lot.Name, spot, LotTime.Format(occurrence.EndAtUtc, lot.TimeZoneId),
            lot.ContactPhone, c.Url($"/account/bookings/{reservation.Id}")), ct);
    }
}

/// <summary>Sent when a session ends and the customer opted in. The session id is unique per dispatch, so a repeat is dropped.</summary>
public sealed class SessionEndedEmailHandler(AppDb db, Mailer mailer, IConfiguration config, IHostEnvironment env) : IJobHandler<SessionEndedEmail>
{
    public async Task HandleAsync(SessionEndedEmail m, CancellationToken ct)
    {
        var session = await db.ParkingSessions.Find(s => s.Id == m.SessionId).Project(s => new { s.OccurrenceId, s.ExitedAtUtc }).FirstOrDefaultAsync(ct);
        if (session?.ExitedAtUtc is not { } endedAt) return;
        var reservationId = await db.Occurrences.Find(o => o.Id == session.OccurrenceId).Project(o => (Guid?)o.ReservationId).FirstOrDefaultAsync(ct);
        if (reservationId is null) return;
        var booking = await db.Reservations.Find(r => r.Id == reservationId).Project(r => new { r.UserId, r.LotId }).FirstOrDefaultAsync(ct);
        if (booking is null) return;
        var user = await db.Users.Find(u => u.Id == booking.UserId).Project(u => new { u.Email, u.Name, u.Preferences.EmailOnCheckout }).FirstOrDefaultAsync(ct);
        if (user is not { EmailOnCheckout: true }) return;
        var lot = await db.Lots.Find(l => l.Id == booking.LotId).Project(l => new { l.Name, l.TimeZoneId }).FirstOrDefaultAsync(ct);
        var c = EmailContext.From(config, env);
        await mailer.SendAsync(Emails.SessionEnded(c, user.Email, user.Name, lot?.Name ?? "the lot", endedAt,
            c.Url($"/account/bookings/{reservationId}"), c.Url($"/account/bookings/{reservationId}/receipt.pdf"), lot?.TimeZoneId), ct);
    }
}

/// <summary>Refund paid notification; the alert is idempotent by source key and the email respects the emailOnRefund preference.</summary>
public sealed class RefundPaidEmailHandler(AppDb db, Mailer mailer, AlertsService alerts, IConfiguration config, IHostEnvironment env) : IJobHandler<RefundPaidEmail>
{
    public async Task HandleAsync(RefundPaidEmail m, CancellationToken ct)
    {
        var refund = await db.Refunds.Find(x => x.Id == m.RefundId).FirstOrDefaultAsync(ct);
        if (refund is not { Status: RefundStatus.Paid }) return;
        var user = await db.Users.Find(x => x.Id == refund.RequestedByUserId).FirstOrDefaultAsync(ct);
        if (user is null) return;
        await alerts.CreateAsync(user.Id, "refund", "Refund paid", $"Your refund of {Display.Money(refund.AmountCents)} has been paid.", "/account/payments", ct: ct, sourceKey: $"refund-paid:{refund.Id}");
        if (!user.Preferences.EmailOnRefund) return;
        var c = EmailContext.From(config, env);
        await mailer.SendAsync(Emails.RefundPaid(c, user.Email, user.Name, refund.AmountCents, refund.ReservationId,
            c.Url("/account/payments"), c.Url($"/account/bookings/{refund.ReservationId}/refund-receipt.pdf")), ct);
    }
}

/// <summary>Booking confirmation. Runwick's uniqueness key stops a second dispatch for the same booking. Delivery is at least once:
/// a worker that dies after sending but before recording success sends it again. A retry skips a booking cancelled meanwhile.</summary>
public sealed class BookingConfirmedEmailHandler(AppDb db, Mailer mailer, IConfiguration config, IHostEnvironment env) : IJobHandler<BookingConfirmedEmail>
{
    public async Task HandleAsync(BookingConfirmedEmail m, CancellationToken ct)
    {
        var reservation = await db.Reservations.Find(x => x.Id == m.ReservationId).FirstOrDefaultAsync(ct);
        if (reservation is not { Status: ReservationStatus.Confirmed }) return;
        var user = await db.Users.Find(x => x.Id == reservation.UserId).FirstOrDefaultAsync(ct);
        if (user is null) return;
        var lot = await db.Lots.Find(x => x.Id == reservation.LotId).Project(x => new { x.Name, x.TimeZoneId }).FirstOrDefaultAsync(ct);
        if (lot is null) return;
        var spot = await db.Spots.Find(x => x.Id == reservation.SpotId).Project(x => x.Label).FirstOrDefaultAsync(ct) ?? "—";
        var payment = await db.Payments.Find(p => p.ReservationId == reservation.Id).SortByDescending(p => p.CreatedAt)
            .Project(p => new { p.Method, p.Status }).FirstOrDefaultAsync(ct);
        var paymentNote = payment switch
        {
            { Status: PaymentStatus.Paid } => "paid",
            { Method: PaymentMethod.PayAtGate } => "pay at the gate",
            _ => "payment pending, finish it from the booking page",
        };
        var when = reservation is { WindowType: WindowType.Recurring, Pattern: { } pattern }
            ? $"{Weekdays(pattern.DaysOfWeek)} {pattern.TimeFrom}–{pattern.TimeTo} ({LotTime.ZoneName(lot.TimeZoneId)}), {LotTime.Span(reservation.StartAtUtc, reservation.EndAtUtc, lot.TimeZoneId)}"
            : LotTime.Span(reservation.StartAtUtc, reservation.EndAtUtc, lot.TimeZoneId);
        var c = EmailContext.From(config, env);
        await mailer.SendAsync(Emails.BookingConfirmed(c, user.Email, user.Name, lot.Name, spot, when, reservation.TotalCents, paymentNote,
            c.Url($"/account/bookings/{reservation.Id}"), c.Url($"/account/bookings/{reservation.Id}/receipt.pdf")), ct);
    }

    static string Weekdays(int[] days) =>
        string.Join(", ", days.Order().Select(d => ((DayOfWeek)d).ToString()[..3]));
}

/// <summary>Sent per occurrence by the reservation sweeper. The alert it leaves (body marker plus a unique source key) marks the
/// occurrence as swept and lets a retried job see that the mail already went out.</summary>
public sealed class ReservationReminderEmailHandler(AppDb db, Mailer mailer, AlertsService alerts, IConfiguration config, IHostEnvironment env) : IJobHandler<ReservationReminderEmail>
{
    public async Task HandleAsync(ReservationReminderEmail m, CancellationToken ct)
    {
        var reservation = await db.Reservations.Find(x => x.Id == m.ReservationId).FirstOrDefaultAsync(ct);
        var occurrence = await db.Occurrences.Find(x => x.Id == m.OccurrenceId).FirstOrDefaultAsync(ct);
        if (reservation is not { Status: ReservationStatus.Confirmed } || occurrence is not { Status: OccurrenceStatus.Scheduled }) return;
        var user = await db.Users.Find(x => x.Id == reservation.UserId).FirstOrDefaultAsync(ct);
        if (user is null || !user.Preferences.BookingReminders) return;
        var sourceKey = $"reminder:{occurrence.Id}";
        if (await db.Alerts.Find(a => a.UserId == user.Id && a.SourceKey == sourceKey).AnyAsync(ct)) return;
        var lot = await db.Lots.Find(x => x.Id == reservation.LotId).Project(x => new { x.Name, x.TimeZoneId }).FirstOrDefaultAsync(ct);
        if (lot is null) return;
        var spot = await db.Spots.Find(x => x.Id == reservation.SpotId).Project(x => x.Label).FirstOrDefaultAsync(ct) ?? "your spot";
        var href = $"/account/bookings/{reservation.Id}";
        var c = EmailContext.From(config, env);
        await mailer.SendAsync(Emails.ReservationReminder(c, user.Email, user.Name, lot.Name, spot, occurrence.StartAtUtc, c.Url(href), lot.TimeZoneId), ct);
        await alerts.CreateAsync(user.Id, "reservation_soon", "Parking reminder", $"occurrence:{occurrence.Id}", href, ct: ct, sourceKey: sourceKey);
    }
}

/// <summary>Claim decision email, sent when an admin pays or rejects a claim; the alert is idempotent by source key.</summary>
public sealed class ClaimDecisionEmailHandler(AppDb db, Mailer mailer, AlertsService alerts, IConfiguration config, IHostEnvironment env) : IJobHandler<ClaimDecisionEmail>
{
    public async Task HandleAsync(ClaimDecisionEmail m, CancellationToken ct)
    {
        var claim = await db.PolicyClaims.Find(x => x.Id == m.ClaimId).FirstOrDefaultAsync(ct);
        if (claim is null) return;
        var user = await db.Users.Find(x => x.Id == claim.UserId).FirstOrDefaultAsync(ct);
        if (user is null) return;
        var paid = string.Equals(m.Decision, "paid", StringComparison.OrdinalIgnoreCase);
        await alerts.CreateAsync(user.Id, "claim", paid ? "Claim payout completed" : "Claim decision", claim.DecisionNote ?? (paid ? "Your Spot Cover claim has been paid." : "Your Spot Cover claim has been reviewed."), "/account/insurance", ct: ct, sourceKey: $"claim:{claim.Id}:{m.Decision}");
        var c = EmailContext.From(config, env);
        await mailer.SendAsync(Emails.ClaimDecision(c, user.Email, user.Name, claim.Kind.ToString().ToLowerInvariant(),
            paid, paid ? claim.PayoutCents : null, claim.DecisionNote ?? "", c.Url("/account/insurance")), ct);
    }
}
