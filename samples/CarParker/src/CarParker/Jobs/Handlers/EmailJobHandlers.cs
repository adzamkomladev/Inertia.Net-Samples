using Microsoft.AspNetCore.DataProtection;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features;
using CarParker.Features.Alerts;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
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
