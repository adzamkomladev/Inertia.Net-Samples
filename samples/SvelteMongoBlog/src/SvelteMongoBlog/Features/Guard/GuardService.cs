using Inertia.Net;
using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Infrastructure;
using SvelteMongoBlog.Features.Alerts;
using SvelteMongoBlog.Features.Bookings;
using SvelteMongoBlog.Features.Incidents;
using SvelteMongoBlog.Features.Lots;

namespace SvelteMongoBlog.Features.Guard;

/// <summary>Check-in / check-out engine, the overstay resolver, no-show sweep and incident helpers.
/// Sessions are the truth for occupied spots; cents-long money; grace windows are configured here.</summary>
public static class GuardService
{
    public const int GraceMinutes = 15;         // check-in early/late window around the occurrence start
    public const int OverstayGraceMinutes = 20; // cron alert threshold past the occurrence end
    public const int NoShowMinutes = 30;        // cron: past start + 30 min without a session → no_show

    public static readonly string[] IncidentKinds = ["unauthorized_entry", "theft", "damage", "patrol_note", "other"];

    /// <summary>Resolves the occurrence a car is claiming: same spot, confirmed reservation,
    /// scheduled within the check-in grace. Ties break on closest start.</summary>
    public static async Task<(Occurrence? Occurrence, Reservation? Reservation, string? Error)> ResolveCheckInAsync(
        AppDb db, Guid spotId, DateTime now, CancellationToken ct)
    {
        var from = now.AddMinutes(-GraceMinutes);
        var to = now.AddMinutes(GraceMinutes);
        var reservations = await db.Reservations.Find(r => r.SpotId == spotId && r.Status == ReservationStatus.Confirmed).ToListAsync(ct);
        if (reservations.Count == 0) return (null, null, "No confirmed reservation uses this spot near its window.");
        var reservationIds = reservations.Select(r => r.Id).ToArray();
        var candidates = await db.Occurrences.Find(o => reservationIds.Contains(o.ReservationId)
                && o.Status == OccurrenceStatus.Scheduled
                && o.StartAtUtc >= from && o.StartAtUtc <= to)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return (null, null, $"No reservation window matches this spot within ±{GraceMinutes} min of now.");
        var occurrence = candidates.OrderBy(o => Math.Abs((o.StartAtUtc - now).Ticks)).First();
        var reservation = reservations.First(r => r.Id == occurrence.ReservationId);
        return (occurrence, reservation, null);
    }

    public static async Task<IResult> CheckInAsync(CheckInOutInput input, HttpContext ctx, AppDb db,
        RealtimeEvents realtime, CancellationToken ct)
    {
        if (input.LotId is not { } lotId || input.SpotId is not { } spotId)
            return Back().WithErrors(new Dictionary<string, string> { ["spotId"] = "Pick a spot on the grid." });
        var spot = await db.Spots.Find(s => s.Id == spotId && s.LotId == lotId && s.Active).FirstOrDefaultAsync(ct);
        if (spot is null)
            return Back().WithErrors(new Dictionary<string, string> { ["spotId"] = "That spot does not exist." });
        if (spot.State == SpotState.Blocked)
            return Back().WithFlash("toast", new Toast("error", $"Spot {spot.Label} is blocked. Clear it first."));
        if (await HasOpenSessionAsync(db, spotId, ct))
            return Back().WithFlash("toast", new Toast("error", $"Spot {spot.Label} already has an open session."));

        var now = DateTime.UtcNow;
        var (occurrence, reservation, error) = await ResolveCheckInAsync(db, spotId, now, ct);
        if (error is not null)
            return Back().WithFlash("toast", new Toast("error", error));
        if (occurrence is null || reservation is null)
            return Back().WithFlash("toast", new Toast("error", "No reservation to check in."));

        var openDuplicate = await db.ParkingSessions.Find(s => s.OccurrenceId == occurrence.Id && s.Status == SessionStatus.InProgress).AnyAsync(ct);
        if (openDuplicate)
            return Back().WithFlash("toast", new Toast("error", "This window is already checked in."));

        // pay_at_gate bills at the gate: the payment lands as the car rolls in.
        var payment = await db.Payments.Find(p => p.ReservationId == reservation.Id && p.Status == PaymentStatus.Pending).FirstOrDefaultAsync(ct);
        if (payment is not null && payment.Method == PaymentMethod.PayAtGate)
            await db.Payments.UpdateOneAsync(
                p => p.Id == payment.Id && p.Status == PaymentStatus.Pending,
                Builders<Payment>.Update
                    .Set(p => p.Status, PaymentStatus.Paid)
                    .Set(p => p.TxRef, $"gate-{payment.Id:N}")
                    .Set(p => p.PaidAt, now),
                cancellationToken: ct);

        var session = new ParkingSession
        {
            OccurrenceId = occurrence.Id,
            SpotId = spotId,
            LotId = lotId,
            PlateNo = (input.PlateNo is { Length: > 0 } plate ? plate : reservation.PlateNo ?? "UNPLATED").ToUpperInvariant(),
            EnteredAtUtc = now,
            EnteredById = ctx.User.UserId(),
        };
        var claim = await db.Spots.UpdateOneAsync(
            s => s.Id == spotId && s.State == SpotState.Booked,
            Builders<Spot>.Update.Set(s => s.State, SpotState.Occupied), cancellationToken: ct);
        if (claim.ModifiedCount == 0)
            return Back().WithFlash("toast", new Toast("error", $"Spot {spot.Label} is not holding a booking. Ask the driver to rebook."));
        await db.Occurrences.UpdateOneAsync(
            o => o.Id == occurrence.Id && o.Status == OccurrenceStatus.Scheduled,
            Builders<Occurrence>.Update.Set(o => o.Status, OccurrenceStatus.Active), cancellationToken: ct);
        await db.ParkingSessions.InsertOneAsync(session, cancellationToken: ct);

        await realtime.SpotChangedAsync(lotId, spotId, "occupied", ct);
        await realtime.BoardChangedAsync("session", ct);
        return Back().WithFlash("toast", new Toast("success", $"Checked in on {spot.Label} ({session.PlateNo})."));
    }

    public static Task<bool> HasOpenSessionAsync(AppDb db, Guid spotId, CancellationToken ct) =>
        db.ParkingSessions.Find(s => s.SpotId == spotId && s.Status == SessionStatus.InProgress).AnyAsync(ct);

    /// <summary>Closes the open session on a spot, marks the occurrence done, frees the spot, completes nothing else —
    /// reservation completion and alert/email land in CloseSessionAsync.</summary>
    public static async Task<IResult> CheckOutAsync(CheckInOutInput input, HttpContext ctx, AppDb db,
        RealtimeEvents realtime, JobQueue jobs, string? reason = null, CancellationToken ct = default)
    {
        if (input.LotId is not { } lotId || input.SpotId is not { } spotId)
            return Back().WithErrors(new Dictionary<string, string> { ["spotId"] = "Pick a spot on the grid." });
        var session = await db.ParkingSessions
            .Find(s => s.SpotId == spotId && s.LotId == lotId && s.Status == SessionStatus.InProgress)
            .SortByDescending(s => s.EnteredAtUtc).FirstOrDefaultAsync(ct);
        if (session is null)
            return Back().WithFlash("toast", new Toast("error", "No open session on that spot."));
        var closed = await CloseSessionAsync(db, jobs, session, ctx.User.UserId(), reason ?? "checkout", realtime, ct);
        if (!closed) return Back().WithFlash("toast", new Toast("info", "That session was closed a moment ago."));

        await realtime.SpotChangedAsync(lotId, spotId, "free", ct);
        await realtime.BoardChangedAsync("session", ct);
        var spot = await db.Spots.Find(s => s.Id == spotId).Project(s => s.Label).FirstOrDefaultAsync(ct);
        return Back().WithFlash("toast", new Toast("success", $"Checked out from {spot ?? "spot"}."));
    }

    /// <summary>Shared close: session + occurrence + spot release + reservation completion + alert/email. Raced closes return false.</summary>
    internal static async Task<bool> CloseSessionAsync(AppDb db, JobQueue jobs, ParkingSession session,
        Guid operatorId, string reason, RealtimeEvents realtime, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var claimed = await db.ParkingSessions.UpdateOneAsync(
            s => s.Id == session.Id && s.Status == SessionStatus.InProgress,
            Builders<ParkingSession>.Update
                .Set(s => s.Status, SessionStatus.Exited)
                .Set(s => s.ExitedAtUtc, now)
                .Set(s => s.ExitedById, operatorId)
                .Set(s => s.CancelReason, reason == "checkout" ? null : reason),
            cancellationToken: ct);
        if (claimed.ModifiedCount == 0) return false;

        await db.Occurrences.UpdateOneAsync(
            o => o.Id == session.OccurrenceId && o.Status == OccurrenceStatus.Active,
            Builders<Occurrence>.Update.Set(o => o.Status, OccurrenceStatus.Done), cancellationToken: ct);
        await db.Spots.UpdateOneAsync(
            s => s.Id == session.SpotId && s.State == SpotState.Occupied,
            Builders<Spot>.Update.Set(s => s.State, SpotState.Free).Set(s => s.LastCheckpointAt, now), cancellationToken: ct);

        var occurrence = await db.Occurrences.Find(o => o.Id == session.OccurrenceId).FirstOrDefaultAsync(ct);
        if (occurrence is null) return true;
        var reservation = await db.Reservations.Find(r => r.Id == occurrence.ReservationId).FirstOrDefaultAsync(ct);
        var remaining = await db.Occurrences.CountDocumentsAsync(
            o => o.ReservationId == occurrence.ReservationId && (o.Status == OccurrenceStatus.Scheduled || o.Status == OccurrenceStatus.Active),
            cancellationToken: ct);
        if (reservation is not null && reservation.Status == ReservationStatus.Confirmed && remaining == 0)
            await db.Reservations.UpdateOneAsync(
                r => r.Id == reservation.Id && r.Status == ReservationStatus.Confirmed,
                Builders<Reservation>.Update
                    .Set(r => r.Status, ReservationStatus.Completed)
                    .Set(r => r.CompletedAt, now), cancellationToken: ct);
        if (reservation is not null)
        {
            var label = await db.Spots.Find(s => s.Id == session.SpotId).Project(s => s.Label).FirstOrDefaultAsync(ct) ?? "spot";
            await AlertsService.CreateAsync(db, realtime, reservation.UserId, "session",
                "Parking session ended", $"Spot {label} is free again.", $"/account/bookings/{reservation.Id}", ct: ct);
            var user = await db.Users.Find(u => u.Id == reservation.UserId).FirstOrDefaultAsync(ct);
            if (user is { } u && u.Preferences.EmailOnCheckout)
                await jobs.EnqueueAsync(db, new SendEmail(u.Email, "Parking session ended",
                    "<p>Thanks for parking with us. Your session just ended.</p>",
                    $"Thanks for parking with us. Your session just ended ({now:u})."), ct: ct);
        }
        return true;
    }

    /// <summary>Guard resolve for an open/overstay session: end it with a "overstay:" note; used from console + board.</summary>
    public static async Task<IResult> ResolveOverstayAsync(Guid sessionId, string? note, HttpContext ctx, AppDb db,
        RealtimeEvents realtime, JobQueue jobs, CancellationToken ct)
    {
        var session = await db.ParkingSessions.Find(s => s.Id == sessionId).FirstOrDefaultAsync(ct);
        if (session is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (session.Status != SessionStatus.InProgress)
            return Back().WithFlash("toast", new Toast("info", "That session already ended."));

        var closed = await CloseSessionAsync(db, jobs, session, ctx.User.UserId(),
            "overstay" + (string.IsNullOrWhiteSpace(note) ? "" : $": {note}"), realtime, ct);
        if (!closed) return Back().WithFlash("toast", new Toast("info", "That session was closed a moment ago."));

        var reservationId = await db.Occurrences.Find(o => o.Id == session.OccurrenceId)
            .Project(o => o.ReservationId).FirstOrDefaultAsync(ct);
        var userId = reservationId == Guid.Empty
            ? Guid.Empty
            : await db.Reservations.Find(r => r.Id == reservationId).Project(r => r.UserId).FirstOrDefaultAsync(ct);
        if (userId != Guid.Empty)
            await AlertsService.CreateAsync(db, realtime, userId, "overstay", "Overstay resolved",
                "A guard ended your open parking session.", "/account/bookings", ct: ct);
        await realtime.SpotChangedAsync(session.LotId, session.SpotId, "free", ct);
        await realtime.BoardChangedAsync("session", ct);
        return Back().WithFlash("toast", new Toast("success", "Overstay resolved — session ended."));
    }

    /// <summary>Overstay flagger: open sessions past end + grace create one user alert + staff push (deduped by session).</summary>
    public static async Task<int> FlagOverstaysAsync(AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var open = await db.ParkingSessions.Find(s => s.Status == SessionStatus.InProgress).ToListAsync(ct);
        if (open.Count == 0) return 0;
        var occurrenceIds = open.Select(s => s.OccurrenceId).ToArray();
        var occurrences = await db.Occurrences.Find(o => occurrenceIds.Contains(o.Id)).ToListAsync(ct);
        var pastEnd = occurrences.Where(o => o.EndAtUtc + TimeSpan.FromMinutes(OverstayGraceMinutes) < now)
            .Select(o => o.Id).ToHashSet();
        var flagged = 0;
        foreach (var session in open.Where(s => pastEnd.Contains(s.OccurrenceId)))
        {
            var userId = await db.Occurrences.Find(o => o.Id == session.OccurrenceId)
                .Project(o => o.ReservationId).FirstOrDefaultAsync(ct) is { } reservationId
                ? await db.Reservations.Find(r => r.Id == reservationId).Project(r => r.UserId).FirstOrDefaultAsync(ct)
                : Guid.Empty;
            if (userId != Guid.Empty
                && !await db.Alerts.Find(a => a.UserId == userId && a.Kind == "overstay"
                    && a.Body == $"session:{session.Id}").AnyAsync(ct))
            {
                await AlertsService.CreateAsync(db, realtime, userId, "overstay", "Overstay alert",
                    $"session:{session.Id}", "/account/bookings", unread: true, ct);
                flagged++;
            }
            await AlertsService.StaffPushAsync(realtime,
                "Overstay: a session is open past its window", "/guard", ct);
        }
        return flagged;
    }

    /// <summary>No-show sweep: scheduled occurrences past start + 30 min with no session → no_show, spot released
    /// when nothing else holds it, refund request per rule when the reservation was paid.</summary>
    public static async Task<int> SweepNoShowsAsync(AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-NoShowMinutes);
        var overdue = await db.Occurrences.Find(o => o.Status == OccurrenceStatus.Scheduled && o.StartAtUtc < cutoff).ToListAsync(ct);
        var flagged = 0;
        foreach (var occurrence in overdue)
        {
            if (await db.ParkingSessions.Find(s => s.OccurrenceId == occurrence.Id).AnyAsync(ct)) continue;
            var reservation = await db.Reservations.Find(r => r.Id == occurrence.ReservationId && r.Status == ReservationStatus.Confirmed).FirstOrDefaultAsync(ct);
            if (reservation is null) continue;

            var marked = await db.Occurrences.UpdateOneAsync(
                o => o.Id == occurrence.Id && o.Status == OccurrenceStatus.Scheduled,
                Builders<Occurrence>.Update.Set(o => o.Status, OccurrenceStatus.NoShow), cancellationToken: ct);
            if (marked.ModifiedCount == 0) continue;
            flagged++;

            // Release the spot only when the reservation is over, nothing parks on the spot, and no other
            // confirmed reservation still holds it for a live window.
            var stillHeld = await db.Reservations.Find(r => r.Id != reservation.Id && r.SpotId == reservation.SpotId
                    && r.Status == ReservationStatus.Confirmed
                    && (r.EndAtUtc == null || r.EndAtUtc >= DateTime.UtcNow)).AnyAsync(ct);
            if (!stillHeld && !await HasOpenSessionAsync(db, reservation.SpotId, ct))
                await db.Spots.UpdateOneAsync(
                    s => s.Id == reservation.SpotId && s.State == SpotState.Booked,
                    Builders<Spot>.Update.Set(s => s.State, SpotState.Free), cancellationToken: ct);

            var payment = await db.Payments.Find(p => p.ReservationId == reservation.Id && p.Status == PaymentStatus.Paid).FirstOrDefaultAsync(ct);
            if (payment is not null)
            {
                var (rule, amount) = RefundRules.ComputeCents(occurrence.StartAtUtc, reservation.TotalCents, DateTime.UtcNow);
                if (amount > 0)
                    await db.Refunds.InsertOneAsync(new Refund
                    {
                        ReservationId = reservation.Id,
                        PaymentId = payment.Id,
                        RequestedByUserId = reservation.UserId,
                        Rule = rule,
                        AmountCents = amount,
                        Reason = $"No-show on window starting {occurrence.StartAtUtc:u}",
                    }, cancellationToken: ct);
            }

            await AlertsService.CreateAsync(db, realtime, reservation.UserId, "gate", "No-show recorded",
                $"Your spot window that started {occurrence.StartAtUtc:u} was released after {NoShowMinutes} minutes.",
                "/account/bookings", ct: ct);
        }
        return flagged;
    }

    // ---------- incidents ----------

    public static async Task<SecurityIncident[]> IncidentsAsync(AppDb db, Guid? lotId, string? status, CancellationToken ct)
    {
        var filter = Builders<SecurityIncident>.Filter.Empty;
        if (lotId is { } id) filter &= Builders<SecurityIncident>.Filter.Eq(i => i.LotId, id);
        if (status is { Length: > 0 } && Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var wanted))
            filter &= Builders<SecurityIncident>.Filter.Eq(i => i.Status, wanted);
        return (await db.SecurityIncidents.Find(filter).SortByDescending(i => i.CreatedAt).Limit(200).ToListAsync(ct)).ToArray();
    }

    public static async Task<IResult> CreateIncidentAsync(IncidentInput input, HttpContext ctx, AppDb db,
        RealtimeEvents realtime, CancellationToken ct)
    {
        if (input.LotId is not { } lotId)
            return Back().WithErrors(new Dictionary<string, string> { ["lotId"] = "Pick the lot." });
        if (!IncidentKinds.Contains(input.Kind, StringComparer.OrdinalIgnoreCase))
            return Back().WithErrors(new Dictionary<string, string> { ["kind"] = "Choose an incident kind." });
        var incident = new SecurityIncident
        {
            LotId = lotId,
            ReporterUserId = ctx.User.UserId(),
            Kind = input.Kind!.ToLowerInvariant(),
            PlateNo = input.PlateNo is { Length: > 0 } ? input.PlateNo.ToUpperInvariant() : null,
            Description = input.Description!.Trim(),
        };
        await db.SecurityIncidents.InsertOneAsync(incident, cancellationToken: ct);
        await realtime.BoardChangedAsync("incident", ct);
        return Back().WithFlash("toast", new Toast("success", "Incident logged."));
    }

    public static async Task<IResult> ResolveIncidentAsync(Guid id, IncidentInput input, HttpContext ctx, AppDb db,
        RealtimeEvents realtime, CancellationToken ct)
    {
        var incident = await db.SecurityIncidents.Find(i => i.Id == id).FirstOrDefaultAsync(ct);
        if (incident is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        var resolved = await db.SecurityIncidents.UpdateOneAsync(
            i => i.Id == id && i.Status == IncidentStatus.Open,
            Builders<SecurityIncident>.Update
                .Set(i => i.Status, IncidentStatus.Resolved)
                .Set(i => i.Note, input.Note)
                .Set(i => i.ResolvedAt, DateTime.UtcNow),
            cancellationToken: ct);
        if (resolved.ModifiedCount == 0)
            return Back().WithFlash("toast", new Toast("info", "That incident was already resolved."));
        await realtime.BoardChangedAsync("incident", ct);
        return Back().WithFlash("toast", new Toast("success", "Incident resolved."));
    }
}
