using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Alerts;
using CarParker.Features.Analytics;
using CarParker.Features.Bookings;
using CarParker.Features.Lots;
using CarParker.Infrastructure;
using Inertia.Net;

namespace CarParker.Features.Guard;

/// <summary>Check-in/check-out, overstay and no-show handling, incidents and the console reads.
/// Sessions are the truth for occupied spots.</summary>
public sealed class GuardService(AppDb db, JobQueue jobs, RealtimeEvents realtime, AlertsService alerts, LotsService lots, IInertiaCache cache)
{
    public const int GraceMinutes = 15;         // check-in window around an occurrence start
    public const int OverstayGraceMinutes = 20; // cron alerts this long after the occurrence end
    public const int NoShowMinutes = 30;        // cron marks no_show this long after the start without a session

    public static readonly string[] IncidentKinds = ["unauthorized_entry", "theft", "damage", "patrol_note", "other"];

    public async Task<Outcome> CheckInAsync(CheckInOutInput input, Guid operatorId, CancellationToken ct)
    {
        if (input.LotId is not { } lotId || input.SpotId is not { } spotId)
            return Outcome.Fail("spotId", "Pick a spot on the grid.");
        var spot = await db.Spots.Find(s => s.Id == spotId && s.LotId == lotId && s.Active).FirstOrDefaultAsync(ct);
        if (spot is null) return Outcome.Fail("spotId", "That spot does not exist.");
        if (spot.State == SpotState.Blocked) return Error($"Spot {spot.Label} is blocked. Clear it first.");
        if (await db.HasOpenSessionAsync(spotId, ct)) return Error($"Spot {spot.Label} already has an open session.");

        // The closest scheduled window of a confirmed reservation on this spot, within the grace window.
        var now = DateTime.UtcNow;
        var reservations = await db.Reservations.Find(r => r.SpotId == spotId && r.Status == ReservationStatus.Confirmed).ToListAsync(ct);
        if (reservations.Count == 0) return Error("No confirmed reservation uses this spot near its window.");
        var reservationIds = reservations.Select(r => r.Id).ToArray();
        var from = now.AddMinutes(-GraceMinutes);
        var to = now.AddMinutes(GraceMinutes);
        var occurrence = (await db.Occurrences.Find(o => reservationIds.Contains(o.ReservationId)
                && o.Status == OccurrenceStatus.Scheduled && o.StartAtUtc >= from && o.StartAtUtc <= to).ToListAsync(ct))
            .MinBy(o => Math.Abs((o.StartAtUtc - now).Ticks));
        if (occurrence is null) return Error($"No reservation window matches this spot within ±{GraceMinutes} min of now.");
        var reservation = reservations.First(r => r.Id == occurrence.ReservationId);

        var claim = await db.Spots.UpdateOneAsync(
            s => s.Id == spotId && s.State == SpotState.Booked,
            Builders<Spot>.Update.Set(s => s.State, SpotState.Occupied), cancellationToken: ct);
        if (claim.ModifiedCount == 0) return Error($"Spot {spot.Label} is not holding a booking. Ask the driver to rebook.");

        // pay_at_gate bills at the gate: the payment lands as the car rolls in.
        var payment = await db.Payments.Find(p => p.ReservationId == reservation.Id && p.Status == PaymentStatus.Pending && p.Method == PaymentMethod.PayAtGate).FirstOrDefaultAsync(ct);
        if (payment is not null)
            await db.Payments.UpdateOneAsync(
                p => p.Id == payment.Id && p.Status == PaymentStatus.Pending,
                Builders<Payment>.Update.Set(p => p.Status, PaymentStatus.Paid).Set(p => p.TxRef, $"gate-{payment.Id:N}").Set(p => p.PaidAt, now),
                cancellationToken: ct);

        var session = new ParkingSession
        {
            OccurrenceId = occurrence.Id,
            SpotId = spotId,
            LotId = lotId,
            PlateNo = (input.PlateNo is { Length: > 0 } plate ? plate : reservation.PlateNo ?? "UNPLATED").ToUpperInvariant(),
            EnteredAtUtc = now,
            EnteredById = operatorId,
        };
        await db.Occurrences.UpdateOneAsync(o => o.Id == occurrence.Id && o.Status == OccurrenceStatus.Scheduled,
            Builders<Occurrence>.Update.Set(o => o.Status, OccurrenceStatus.Active), cancellationToken: ct);
        await db.ParkingSessions.InsertOneAsync(session, cancellationToken: ct);

        await realtime.SpotChangedAsync(lotId, spotId, "occupied", ct);
        await realtime.BoardChangedAsync("session", ct);
        await cache.RemoveByTagAsync(AnalyticsCache.Tag, ct);
        return Outcome.FlashBack(new Toast("success", $"Checked in on {spot.Label} ({session.PlateNo})."));
    }

    public async Task<Outcome> CheckOutAsync(CheckInOutInput input, Guid operatorId, CancellationToken ct)
    {
        if (input.LotId is not { } lotId || input.SpotId is not { } spotId)
            return Outcome.Fail("spotId", "Pick a spot on the grid.");
        var session = await db.ParkingSessions.Find(s => s.SpotId == spotId && s.LotId == lotId && s.Status == SessionStatus.InProgress)
            .SortByDescending(s => s.EnteredAtUtc).FirstOrDefaultAsync(ct);
        if (session is null) return Error("No open session on that spot.");
        if (!await CloseSessionAsync(session, operatorId, "checkout", ct))
            return Outcome.FlashBack(new Toast("info", "That session was closed a moment ago."));

        await realtime.SpotChangedAsync(lotId, spotId, "free", ct);
        await realtime.BoardChangedAsync("session", ct);
        await cache.RemoveByTagAsync(AnalyticsCache.Tag, ct);
        var label = await db.Spots.Find(s => s.Id == spotId).Project(s => s.Label).FirstOrDefaultAsync(ct);
        return Outcome.FlashBack(new Toast("success", $"Checked out from {label ?? "spot"}."));
    }

    /// <summary>Ends a session: occurrence done, spot freed, reservation completed when nothing is left, then alert and email. A raced close returns false.</summary>
    public async Task<bool> CloseSessionAsync(ParkingSession session, Guid operatorId, string reason, CancellationToken ct)
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

        var occurrence = await db.Occurrences.FindOneAndUpdateAsync(
            o => o.Id == session.OccurrenceId && o.Status == OccurrenceStatus.Active,
            Builders<Occurrence>.Update.Set(o => o.Status, OccurrenceStatus.Done),
            new FindOneAndUpdateOptions<Occurrence> { ReturnDocument = ReturnDocument.After }, ct)
            ?? await db.Occurrences.Find(o => o.Id == session.OccurrenceId).FirstOrDefaultAsync(ct);
        var freed = await db.Spots.FindOneAndUpdateAsync(
            s => s.Id == session.SpotId && s.State == SpotState.Occupied,
            Builders<Spot>.Update.Set(s => s.State, SpotState.Free).Set(s => s.LastCheckpointAt, now),
            new FindOneAndUpdateOptions<Spot> { ReturnDocument = ReturnDocument.After }, ct);
        if (occurrence is null) return true;
        var reservation = await db.Reservations.Find(r => r.Id == occurrence.ReservationId).FirstOrDefaultAsync(ct);
        if (reservation is null) return true;

        if (reservation.Status == ReservationStatus.Confirmed
            && !await db.Occurrences.Find(o => o.ReservationId == reservation.Id
                && (o.Status == OccurrenceStatus.Scheduled || o.Status == OccurrenceStatus.Active)).AnyAsync(ct))
            await db.Reservations.UpdateOneAsync(
                r => r.Id == reservation.Id && r.Status == ReservationStatus.Confirmed,
                Builders<Reservation>.Update.Set(r => r.Status, ReservationStatus.Completed).Set(r => r.CompletedAt, now),
                cancellationToken: ct);

        var label = freed?.Label ?? await db.Spots.Find(s => s.Id == session.SpotId).Project(s => s.Label).FirstOrDefaultAsync(ct) ?? "spot";
        await alerts.CreateAsync(reservation.UserId, "session", "Parking session ended", $"Spot {label} is free again.", $"/account/bookings/{reservation.Id}", ct: ct);
        var user = await db.Users.Find(u => u.Id == reservation.UserId).FirstOrDefaultAsync(ct);
        if (user is { Preferences.EmailOnCheckout: true })
            await jobs.EnqueueAsync(db, new SendEmail(user.Email, "Parking session ended",
                "<p>Thanks for parking with us. Your session just ended.</p>",
                $"Thanks for parking with us. Your session just ended ({now:u})."), ct: ct);
        return true;
    }

    /// <summary>Guard/admin ends an open session. Overstay resolves also tell the customer.</summary>
    public async Task<Outcome> EndSessionAsync(Guid sessionId, string reasonPrefix, string? note, Guid operatorId, string successMessage, CancellationToken ct)
    {
        var session = await db.ParkingSessions.Find(s => s.Id == sessionId).FirstOrDefaultAsync(ct);
        if (session is null) return Outcome.Missing();
        if (session.Status != SessionStatus.InProgress)
            return Outcome.FlashBack(new Toast("info", "That session already ended."));
        var reason = reasonPrefix + (string.IsNullOrWhiteSpace(note) ? "" : $": {note}");
        if (!await CloseSessionAsync(session, operatorId, reason, ct))
            return Outcome.FlashBack(new Toast("info", "That session was closed a moment ago."));

        if (reasonPrefix == "overstay")
        {
            var userId = await UserIdsByOccurrenceAsync([session.OccurrenceId], ct);
            if (userId.TryGetValue(session.OccurrenceId, out var customerId))
                await alerts.CreateAsync(customerId, "overstay", "Overstay resolved", "A guard ended your open parking session.", "/account/bookings", ct: ct);
        }
        await realtime.SpotChangedAsync(session.LotId, session.SpotId, "free", ct);
        await realtime.BoardChangedAsync("session", ct);
        await cache.RemoveByTagAsync(AnalyticsCache.Tag, ct);
        return Outcome.FlashBack(new Toast("success", successMessage));
    }

    public async Task<Outcome> ResolveOverstayAsync(Guid sessionId, string? note, Guid operatorId, CancellationToken ct) =>
        await EndSessionAsync(sessionId, "overstay", note, operatorId, "Overstay resolved. Session ended.", ct);

    public async Task<Outcome> ForceCloseAsync(Guid sessionId, string? note, Guid operatorId, CancellationToken ct) =>
        await EndSessionAsync(sessionId, "force_close", note, operatorId, "Session force-closed.", ct);

    /// <summary>Occurrence id to owning user id, in two round trips for any number of occurrences.</summary>
    async Task<Dictionary<Guid, Guid>> UserIdsByOccurrenceAsync(Guid[] occurrenceIds, CancellationToken ct)
    {
        var reservationByOccurrence = (await db.Occurrences.Find(o => occurrenceIds.Contains(o.Id)).Project(o => new { o.Id, o.ReservationId }).ToListAsync(ct))
            .ToDictionary(o => o.Id, o => o.ReservationId);
        var reservationIds = reservationByOccurrence.Values.Distinct().ToArray();
        var userByReservation = (await db.Reservations.Find(r => reservationIds.Contains(r.Id)).Project(r => new { r.Id, r.UserId }).ToListAsync(ct))
            .ToDictionary(r => r.Id, r => r.UserId);
        return reservationByOccurrence.Where(p => userByReservation.ContainsKey(p.Value))
            .ToDictionary(p => p.Key, p => userByReservation[p.Value]);
    }

    /// <summary>Open sessions past their window plus grace get one unread alert each (deduped by session) and a staff push.</summary>
    public async Task<int> FlagOverstaysAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var open = await db.ParkingSessions.Find(s => s.Status == SessionStatus.InProgress).ToListAsync(ct);
        if (open.Count == 0) return 0;
        var occurrenceIds = open.Select(s => s.OccurrenceId).ToArray();
        var cutoff = now.AddMinutes(-OverstayGraceMinutes);
        var pastEnd = (await db.Occurrences.Find(o => occurrenceIds.Contains(o.Id) && o.EndAtUtc < cutoff).Project(o => o.Id).ToListAsync(ct)).ToHashSet();
        var overdue = open.Where(s => pastEnd.Contains(s.OccurrenceId)).ToList();
        if (overdue.Count == 0) return 0;

        var users = await UserIdsByOccurrenceAsync([.. pastEnd], ct);
        var markers = overdue.Select(s => $"session:{s.Id}").ToArray();
        var alreadyAlerted = (await db.Alerts.Find(a => a.Kind == "overstay" && markers.Contains(a.Body!)).Project(a => a.Body).ToListAsync(ct)).ToHashSet();
        var fresh = overdue.Where(s => users.ContainsKey(s.OccurrenceId) && !alreadyAlerted.Contains($"session:{s.Id}"))
            .Select(s => AlertsService.NewAlert(users[s.OccurrenceId], "overstay", "Overstay alert", $"session:{s.Id}", "/account/bookings", unread: true))
            .ToList();
        await alerts.CreateManyAsync(fresh, ct);
        if (fresh.Count > 0) await alerts.NotifyStaffAsync("Overstay: a session is open past its window", "/guard", ct);
        return fresh.Count;
    }

    /// <summary>Scheduled occurrences past start + 30 min with no session become no_show; the spot is released when nothing
    /// else holds it, a refund is requested by rule when the reservation was paid, and the customer is told.</summary>
    public async Task<int> SweepNoShowsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-NoShowMinutes);
        var overdue = await db.Occurrences.Find(o => o.Status == OccurrenceStatus.Scheduled && o.StartAtUtc < cutoff).ToListAsync(ct);
        if (overdue.Count == 0) return 0;

        var occurrenceIds = overdue.Select(o => o.Id).ToArray();
        var withSession = (await db.ParkingSessions.Distinct(s => s.OccurrenceId, s => occurrenceIds.Contains(s.OccurrenceId)).ToListAsync(ct)).ToHashSet();
        var reservationIds = overdue.Select(o => o.ReservationId).Distinct().ToArray();
        var reservations = (await db.Reservations.Find(r => reservationIds.Contains(r.Id) && r.Status == ReservationStatus.Confirmed).ToListAsync(ct))
            .ToDictionary(r => r.Id);
        var paid = (await db.Payments.Find(p => reservationIds.Contains(p.ReservationId) && p.Status == PaymentStatus.Paid).ToListAsync(ct))
            .GroupBy(p => p.ReservationId).ToDictionary(g => g.Key, g => g.First());

        var noShows = new List<Reservation>();
        var refunds = new List<Refund>();
        var notices = new List<Alert>();
        foreach (var occurrence in overdue)
        {
            if (withSession.Contains(occurrence.Id) || !reservations.TryGetValue(occurrence.ReservationId, out var reservation)) continue;
            var marked = await db.Occurrences.UpdateOneAsync(o => o.Id == occurrence.Id && o.Status == OccurrenceStatus.Scheduled,
                Builders<Occurrence>.Update.Set(o => o.Status, OccurrenceStatus.NoShow), cancellationToken: ct);
            if (marked.ModifiedCount == 0) continue;

            noShows.Add(reservation);
            if (paid.TryGetValue(reservation.Id, out var payment))
            {
                var (rule, amount) = RefundRules.ComputeCents(occurrence.StartAtUtc, reservation.TotalCents, now);
                if (amount > 0)
                    refunds.Add(new Refund
                    {
                        ReservationId = reservation.Id, PaymentId = payment.Id, RequestedByUserId = reservation.UserId,
                        Rule = rule, AmountCents = amount, Reason = $"No-show on window starting {occurrence.StartAtUtc:u}",
                    });
            }
            notices.Add(AlertsService.NewAlert(reservation.UserId, "gate", "No-show recorded",
                $"Your spot window that started {occurrence.StartAtUtc:u} was released after {NoShowMinutes} minutes.", "/account/bookings"));
        }

        if (noShows.Count > 0)
        {
            // Read holders after marking, not before: a booking made while the sweep ran must keep its spot.
            // ponytail: still check-then-act; a booking whose transaction commits between this read and the update can
            // lose its spot. Release atomically per spot (e.g. a holder id on Spot) if that window matters.
            var spotIds = noShows.Select(r => r.SpotId).Distinct().ToArray();
            var noShowIds = noShows.Select(r => r.Id).ToArray();
            var held = await db.Reservations.Distinct(r => r.SpotId, r => spotIds.Contains(r.SpotId) && !noShowIds.Contains(r.Id)
                && r.Status == ReservationStatus.Confirmed && (r.EndAtUtc == null || r.EndAtUtc >= now)).ToListAsync(ct);
            var parked = await db.ParkingSessions.Distinct(s => s.SpotId, s => spotIds.Contains(s.SpotId) && s.Status == SessionStatus.InProgress).ToListAsync(ct);
            var released = spotIds.Except(held).Except(parked).ToArray();
            await db.Spots.UpdateManyAsync(s => released.Contains(s.Id) && s.State == SpotState.Booked,
                Builders<Spot>.Update.Set(s => s.State, SpotState.Free), cancellationToken: ct);
        }
        if (refunds.Count > 0) await db.Refunds.InsertManyAsync(refunds, cancellationToken: ct);
        await alerts.CreateManyAsync(notices, ct);
        return notices.Count;
    }

    // ---------- incidents ----------

    public async Task<IncidentsPage> IncidentsPageAsync(Guid? lotId, string? status, string? q, int page, CancellationToken ct)
    {
        const int pageSize = 25;
        page = Math.Max(page, 1);
        var filter = Builders<SecurityIncident>.Filter.Empty;
        if (lotId is { } id) filter &= Builders<SecurityIncident>.Filter.Eq(i => i.LotId, id);
        if (Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var wanted))
            filter &= Builders<SecurityIncident>.Filter.Eq(i => i.Status, wanted);
        var active = await lots.ActiveAsync(ct);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = new MongoDB.Bson.BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(q.Trim()), "i");
            var matchedLots = await db.Lots.Find(Builders<Lot>.Filter.Regex(l => l.Name, pattern)).Project(l => l.Id).ToListAsync(ct);
            var matchedReporters = await db.Users.Find(Builders<User>.Filter.Regex(u => u.Name, pattern)).Project(u => u.Id).ToListAsync(ct);
            filter &= Builders<SecurityIncident>.Filter.Regex(i => i.Kind, pattern)
                | Builders<SecurityIncident>.Filter.Regex(i => i.PlateNo, pattern)
                | Builders<SecurityIncident>.Filter.Regex(i => i.Description, pattern)
                | Builders<SecurityIncident>.Filter.In(i => i.LotId, matchedLots)
                | Builders<SecurityIncident>.Filter.In(i => i.ReporterUserId, matchedReporters);
        }
        var rows = await db.SecurityIncidents.Find(filter).SortByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id)
            .Skip((int)Math.Min(((long)page - 1) * pageSize, int.MaxValue)).Limit(pageSize + 1).ToListAsync(ct);
        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(pageSize);
        var reporterIds = rows.Select(i => i.ReporterUserId).Distinct().ToArray();
        var reporters = (await db.Users.Find(u => reporterIds.Contains(u.Id)).Project(u => new { u.Id, u.Name }).ToListAsync(ct))
            .ToDictionary(u => u.Id, u => u.Name);
        var lotNames = active.ToDictionary(l => l.Id, l => l.Name);
        return new IncidentsPage(LotsMappers.OptionsFor(active),
            rows.Select(i => new IncidentRow(i.Id, i.LotId, i.Kind, i.PlateNo, i.Description, i.Note,
                i.Status.ToString().ToLowerInvariant(), i.CreatedAt, i.ResolvedAt,
                lotNames.GetValueOrDefault(i.LotId), reporters.GetValueOrDefault(i.ReporterUserId))).ToArray(), page, hasMore);
    }

    public async Task<Outcome> CreateIncidentAsync(IncidentInput input, Guid reporterId, CancellationToken ct)
    {
        if (input.LotId is not { } lotId) return Outcome.Fail("lotId", "Pick the lot.");
        if (!IncidentKinds.Contains(input.Kind, StringComparer.OrdinalIgnoreCase))
            return Outcome.Fail("kind", "Choose an incident kind.");
        await db.SecurityIncidents.InsertOneAsync(new SecurityIncident
        {
            LotId = lotId,
            ReporterUserId = reporterId,
            Kind = input.Kind!.ToLowerInvariant(),
            PlateNo = input.PlateNo is { Length: > 0 } ? input.PlateNo.ToUpperInvariant() : null,
            Description = input.Description!.Trim(),
        }, cancellationToken: ct);
        await realtime.BoardChangedAsync("incident", ct);
        return Outcome.FlashBack(new Toast("success", "Incident logged."));
    }

    public async Task<Outcome> ResolveIncidentAsync(Guid id, string? note, CancellationToken ct)
    {
        var resolved = await db.SecurityIncidents.UpdateOneAsync(i => i.Id == id && i.Status == IncidentStatus.Open,
            Builders<SecurityIncident>.Update.Set(i => i.Status, IncidentStatus.Resolved).Set(i => i.Note, note).Set(i => i.ResolvedAt, DateTime.UtcNow),
            cancellationToken: ct);
        if (resolved.MatchedCount == 0 && !await db.SecurityIncidents.Find(i => i.Id == id).AnyAsync(ct)) return Outcome.Missing();
        if (resolved.ModifiedCount == 0) return Outcome.FlashBack(new Toast("info", "That incident was already resolved."));
        await realtime.BoardChangedAsync("incident", ct);
        return Outcome.FlashBack(new Toast("success", "Incident resolved."));
    }

    // ---------- console & board ----------

    /// <summary>The chosen lot's live grid, today's queue and open sessions. Null when no lot exists.</summary>
    public async Task<ConsolePage?> ConsoleAsync(Guid? lot, CancellationToken ct)
    {
        var active = await lots.ActiveAsync(ct);
        var chosen = active.FirstOrDefault(l => l.Id == lot) ?? active.FirstOrDefault();
        if (chosen is null) return null;

        // The grid, the lot's reservations and its open sessions are independent; only the queue needs both lists.
        var spotsTask = lots.SpotsAsync(chosen.Id, ct);
        var reservationsTask = db.Reservations.Find(r => r.LotId == chosen.Id && r.Status == ReservationStatus.Confirmed)
            .Project(r => new { r.Id, r.SpotId, r.PlateNo }).ToListAsync(ct);
        var openTask = db.ParkingSessions.Find(s => s.LotId == chosen.Id && s.Status == SessionStatus.InProgress)
            .SortByDescending(s => s.EnteredAtUtc).ToListAsync(ct);
        await Task.WhenAll(spotsTask, reservationsTask, openTask);

        var spots = await spotsTask;
        var labels = spots.ToDictionary(s => s.Id, s => s.Label);
        var reservations = (await reservationsTask).ToDictionary(r => r.Id);
        var open = await openTask;
        var reservationIds = reservations.Keys.ToArray();
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var queue = reservationIds.Length == 0 ? new List<Occurrence>() : await db.Occurrences.Find(o => reservationIds.Contains(o.ReservationId)
                && (o.Status == OccurrenceStatus.Scheduled || o.Status == OccurrenceStatus.Active)
                && o.StartAtUtc < tomorrow && o.EndAtUtc >= today)
            .SortBy(o => o.StartAtUtc).ToListAsync(ct);

        return new ConsolePage(LotsMappers.OptionsFor(active), chosen.Id, spots,
            queue.Select(o => new QueueRow(o.Id, o.ReservationId, labels.GetValueOrDefault(reservations[o.ReservationId].SpotId),
                o.StartAtUtc, o.EndAtUtc, o.Status.ToString().ToLowerInvariant(), reservations[o.ReservationId].PlateNo)).ToArray(),
            open.Select(s => new SessionRow(s.Id, labels.GetValueOrDefault(s.SpotId), s.PlateNo, s.EnteredAtUtc, s.CancelReason)).ToArray());
    }

    /// <summary>Open sessions plus today's exited ones across active lots.</summary>
    public async Task<BoardPage> BoardAsync(CancellationToken ct)
    {
        var active = await lots.ActiveAsync(ct);
        var lotIds = active.Select(l => l.Id).ToArray();
        var today = DateTime.UtcNow.Date;
        var openTask = db.ParkingSessions.Find(s => lotIds.Contains(s.LotId) && s.Status == SessionStatus.InProgress)
            .SortBy(s => s.EnteredAtUtc).ToListAsync(ct);
        var exitedTask = db.ParkingSessions.Find(s => lotIds.Contains(s.LotId) && s.Status == SessionStatus.Exited && s.EnteredAtUtc >= today)
            .SortByDescending(s => s.EnteredAtUtc).Limit(50).ToListAsync(ct);
        await Task.WhenAll(openTask, exitedTask);
        var open = await openTask;
        var exited = await exitedTask;
        var spotIds = open.Concat(exited).Select(s => s.SpotId).Distinct().ToArray();
        var labels = spotIds.Length == 0 ? new Dictionary<Guid, string>()
            : (await db.Spots.Find(s => spotIds.Contains(s.Id)).Project(s => new { s.Id, s.Label }).ToListAsync(ct))
                .ToDictionary(s => s.Id, s => s.Label);
        return new BoardPage(LotsMappers.OptionsFor(active),
            open.Select(s => new SessionRow(s.Id, labels.GetValueOrDefault(s.SpotId), s.PlateNo, s.EnteredAtUtc, s.CancelReason)).ToArray(),
            exited.Select(s => new SessionRow(s.Id, labels.GetValueOrDefault(s.SpotId), s.PlateNo, s.EnteredAtUtc, s.CancelReason, s.ExitedAtUtc)).ToArray());
    }

    static Outcome Error(string message) => Outcome.FlashBack(new Toast("error", message));
}
