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
public sealed class GuardService(AppDb db, JobEnqueuer jobs, RealtimeEvents realtime, AlertsService alerts, LotsService lots, IInertiaCache cache)
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
            s => s.Id == spotId && (s.State == SpotState.Booked || s.State == SpotState.Free),
            Builders<Spot>.Update.Set(s => s.State, SpotState.Occupied), cancellationToken: ct);
        if (claim.ModifiedCount == 0) return Error($"Spot {spot.Label} is not available for check-in right now.");

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
        await AnalyticsCache.LotActivityAsync(cache, lotId, ct);
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

        await realtime.SpotChangedAsync(lotId, spotId, await SpotWordAsync(spotId, ct), ct);
        await realtime.BoardChangedAsync("session", ct);
        await AnalyticsCache.LotActivityAsync(cache, lotId, ct);
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
        // The bay goes back to Booked, not Free, while another confirmed reservation still has a window ahead on it
        // (the closed occurrence is Done by now, so it no longer counts).
        var stillHeld = (await db.HeldSpotIdsAsync([session.SpotId], now, ct)).Contains(session.SpotId);
        var freed = await db.Spots.FindOneAndUpdateAsync(
            s => s.Id == session.SpotId && s.State == SpotState.Occupied,
            Builders<Spot>.Update.Set(s => s.State, stillHeld ? SpotState.Booked : SpotState.Free).Set(s => s.LastCheckpointAt, now),
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
        // The handler reads the customer's preference and renders the mail; the session id is its idempotency key.
        await jobs.DispatchAsync(new SessionEndedEmail(session.Id), ct);
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
        await realtime.SpotChangedAsync(session.LotId, session.SpotId, await SpotWordAsync(session.SpotId, ct), ct);
        await realtime.BoardChangedAsync("session", ct);
        await AnalyticsCache.LotActivityAsync(cache, session.LotId, ct);
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
        var open = await db.ParkingSessions.Find(s => s.Status == SessionStatus.InProgress).Project(s => new { s.Id, s.OccurrenceId }).ToListAsync(ct);
        if (open.Count == 0) return 0;
        var occurrenceIds = open.Select(s => s.OccurrenceId).ToArray();
        var cutoff = now.AddMinutes(-OverstayGraceMinutes);
        var pastEnd = (await db.Occurrences.Find(o => occurrenceIds.Contains(o.Id) && o.EndAtUtc < cutoff).Project(o => o.Id).ToListAsync(ct)).ToHashSet();
        var overdue = open.Where(s => pastEnd.Contains(s.OccurrenceId)).ToList();
        if (overdue.Count == 0) return 0;

        var users = await UserIdsByOccurrenceAsync([.. pastEnd], ct);
        var markers = overdue.Select(s => $"session:{s.Id}").ToArray();
        var alreadyAlerted = (await db.Alerts.Find(a => a.Kind == "overstay" && markers.Contains(a.Body!)).Project(a => a.Body).ToListAsync(ct)).ToHashSet();
        var freshSessions = overdue.Where(s => users.ContainsKey(s.OccurrenceId) && !alreadyAlerted.Contains($"session:{s.Id}")).ToList();
        var fresh = freshSessions
            .Select(s => AlertsService.NewAlert(users[s.OccurrenceId], "overstay", "Overstay alert", $"session:{s.Id}", "/account/bookings", unread: true))
            .ToList();
        await alerts.CreateManyAsync(fresh, ct);
        if (fresh.Count == 0) return 0;
        await alerts.NotifyStaffAsync("Overstay: a session is open past its window", "/guard", ct);
        // The driver also gets one email per flagged session (the alert above is the dedupe; the session id keys the job).
        await jobs.DispatchManyAsync([.. freshSessions.Select(s => (Runwick.IQueueJob)new OverstayEmail(s.Id))], ct);
        return fresh.Count;
    }

    const int NoShowBatch = 500;

    /// <summary>Scheduled occurrences past start + 30 min with no session become no_show; the spot is released when nothing
    /// else holds it, a refund is requested by rule when the reservation was paid, and the customer is told.
    /// A backlog is worked in batches of <see cref="NoShowBatch"/>; each batch is one status update, one refund insert and one alert insert.</summary>
    public async Task<int> SweepNoShowsAsync(CancellationToken ct)
    {
        var total = 0;
        for (var round = 0; round < 20; round++)
        {
            var (found, handled) = await SweepNoShowBatchAsync(ct);
            total += handled;
            if (found < NoShowBatch) break;
        }
        return total;
    }

    async Task<(int Found, int Handled)> SweepNoShowBatchAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-NoShowMinutes);
        var overdue = await db.Occurrences.Find(o => o.Status == OccurrenceStatus.Scheduled && o.StartAtUtc < cutoff)
            .SortBy(o => o.StartAtUtc).Limit(NoShowBatch)
            .Project(o => new { o.Id, o.ReservationId, o.StartAtUtc }).ToListAsync(ct);
        if (overdue.Count == 0) return (0, 0);

        var occurrenceIds = overdue.Select(o => o.Id).ToArray();
        var withSession = (await db.ParkingSessions.Distinct(s => s.OccurrenceId, s => occurrenceIds.Contains(s.OccurrenceId)).ToListAsync(ct)).ToHashSet();
        var reservationIds = overdue.Select(o => o.ReservationId).Distinct().ToArray();
        var reservations = (await db.Reservations.Find(r => reservationIds.Contains(r.Id) && r.Status == ReservationStatus.Confirmed)
                .Project(r => new { r.Id, r.UserId, r.SpotId, r.LotId, r.TotalCents }).ToListAsync(ct))
            .ToDictionary(r => r.Id);
        var paid = (await db.Payments.Find(p => reservationIds.Contains(p.ReservationId) && p.Status == PaymentStatus.Paid)
                .Project(p => new { p.Id, p.ReservationId }).ToListAsync(ct))
            .GroupBy(p => p.ReservationId).ToDictionary(g => g.Key, g => g.First());

        // Overdue windows of a reservation that is no longer confirmed are closed as Cancelled, so they stop filling the
        // oldest-first batch and starving newer no-shows. Windows with a session are left to the gate.
        var orphanIds = overdue.Where(o => !withSession.Contains(o.Id) && !reservations.ContainsKey(o.ReservationId)).Select(o => o.Id).ToArray();
        if (orphanIds.Length > 0)
            await db.Occurrences.UpdateManyAsync(o => orphanIds.Contains(o.Id) && o.Status == OccurrenceStatus.Scheduled,
                Builders<Occurrence>.Update.Set(o => o.Status, OccurrenceStatus.Cancelled), cancellationToken: ct);
        var candidates = overdue.Where(o => !withSession.Contains(o.Id) && reservations.ContainsKey(o.ReservationId)).ToList();
        if (candidates.Count == 0) return (overdue.Count, 0);

        // One conditional update for the batch. A check-in that raced the read has already made its occurrence Active and is skipped.
        var candidateIds = candidates.Select(o => o.Id).ToArray();
        var marked = await db.Occurrences.UpdateManyAsync(o => candidateIds.Contains(o.Id) && o.Status == OccurrenceStatus.Scheduled,
            Builders<Occurrence>.Update.Set(o => o.Status, OccurrenceStatus.NoShow), cancellationToken: ct);
        if (marked.ModifiedCount == 0) return (overdue.Count, 0);
        var won = candidates;
        if (marked.ModifiedCount != candidates.Count)
        {
            var noShow = (await db.Occurrences.Find(o => candidateIds.Contains(o.Id) && o.Status == OccurrenceStatus.NoShow).Project(o => o.Id).ToListAsync(ct)).ToHashSet();
            won = candidates.Where(o => noShow.Contains(o.Id)).ToList();
        }

        var lotIds = won.Select(o => reservations[o.ReservationId].LotId).Distinct().ToArray();
        var zones = (await db.Lots.Find(l => lotIds.Contains(l.Id)).Project(l => new { l.Id, l.TimeZoneId }).ToListAsync(ct))
            .ToDictionary(l => l.Id, l => l.TimeZoneId);
        var refunds = new List<Refund>();
        var notices = new List<Alert>();
        foreach (var occurrence in won)
        {
            var reservation = reservations[occurrence.ReservationId];
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
                $"Your bay window that started {LotTime.Format(occurrence.StartAtUtc, zones.GetValueOrDefault(reservation.LotId))} was released after {NoShowMinutes} minutes.", $"/account/bookings/{reservation.Id}"));
        }

        // Read holders after marking, not before: a booking made while the sweep ran must keep its spot.
        // ponytail: still check-then-act; a booking whose transaction commits between this read and the update can
        // lose its spot. Release atomically per spot (e.g. a holder id on Spot) if that window matters.
        // A bay stays Booked while any confirmed reservation (including a later window of the no-show's own) has a window ahead.
        var spotIds = won.Select(o => reservations[o.ReservationId].SpotId).Distinct().ToArray();
        var held = await db.HeldSpotIdsAsync(spotIds, now, ct);
        var parked = await db.ParkingSessions.Distinct(s => s.SpotId, s => spotIds.Contains(s.SpotId) && s.Status == SessionStatus.InProgress).ToListAsync(ct);
        var released = spotIds.Except(held).Except(parked).ToArray();
        await db.Spots.UpdateManyAsync(s => released.Contains(s.Id) && s.State == SpotState.Booked,
            Builders<Spot>.Update.Set(s => s.State, SpotState.Free), cancellationToken: ct);
        if (refunds.Count > 0) await db.Refunds.InsertManyAsync(refunds, cancellationToken: ct);
        await alerts.CreateManyAsync(notices, ct);
        // One email per released window, sent after the refund rows exist so the mail can state the amount.
        await jobs.DispatchManyAsync([.. won.Select(o => (Runwick.IQueueJob)new NoShowEmail(o.Id))], ct);
        return (overdue.Count, notices.Count);
    }
    // ---------- incidents ----------

    public async Task<IncidentsPage> IncidentsPageAsync(Guid? lotId, string? status, string? q, PageRequest request, CancellationToken ct)
    {
        request = request with { PerPage = 25 };
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
        var page = await db.SecurityIncidents.Find(filter).SortByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id)
            .ToPageAsync(request, ct: ct);
        var rows = page.Data;
        var reporterIds = rows.Select(i => i.ReporterUserId).Distinct().ToArray();
        var reporters = (await db.Users.Find(u => reporterIds.Contains(u.Id)).Project(u => new { u.Id, u.Name }).ToListAsync(ct))
            .ToDictionary(u => u.Id, u => u.Name);
        var lotNames = active.ToDictionary(l => l.Id, l => l.Name);
        // Zones for the rows' own lots: an inactive lot's incidents still read on its clock.
        var rowLotIds = rows.Select(i => i.LotId).Distinct().ToArray();
        var lotZones = (await db.Lots.Find(l => rowLotIds.Contains(l.Id)).Project(l => new { l.Id, l.TimeZoneId }).ToListAsync(ct))
            .ToDictionary(l => l.Id, l => l.TimeZoneId);
        return new IncidentsPage(LotsMappers.OptionsFor(active),
            rows.Select(i => new IncidentRow(i.Id, i.LotId, i.Kind, i.PlateNo, i.Description, i.Note,
                i.Status.ToString().ToLowerInvariant(), i.CreatedAt, i.ResolvedAt,
                lotNames.GetValueOrDefault(i.LotId), reporters.GetValueOrDefault(i.ReporterUserId), lotZones.GetValueOrDefault(i.LotId, "UTC"))).ToArray(), page.CurrentPage, page.HasMore);
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
        // Window ends of the parked cars, so the console measures overstay from the booking's end, not the arrival.
        var occurrenceIds = open.Select(s => s.OccurrenceId).Distinct().ToArray();
        var windowEnds = occurrenceIds.Length == 0 ? new Dictionary<Guid, DateTime>()
            : (await db.Occurrences.Find(o => occurrenceIds.Contains(o.Id)).Project(o => new { o.Id, o.EndAtUtc }).ToListAsync(ct))
                .ToDictionary(o => o.Id, o => o.EndAtUtc);

        return new ConsolePage(LotsMappers.OptionsFor(active), chosen.Id, spots,
            queue.Select(o => new QueueRow(o.Id, o.ReservationId, labels.GetValueOrDefault(reservations[o.ReservationId].SpotId),
                o.StartAtUtc, o.EndAtUtc, o.Status.ToString().ToLowerInvariant(), reservations[o.ReservationId].PlateNo)).ToArray(),
            open.Select(s => new SessionRow(s.Id, labels.GetValueOrDefault(s.SpotId), s.PlateNo, s.EnteredAtUtc, s.CancelReason,
                WindowEndUtc: windowEnds.TryGetValue(s.OccurrenceId, out var end) ? end : null)).ToArray(),
            chosen.TimeZoneId);
    }

    /// <summary>Open sessions plus today's exited ones across active lots.</summary>
    public async Task<BoardPage> BoardAsync(CancellationToken ct)
    {
        var active = await lots.ActiveAsync(ct);
        var lotIds = active.Select(l => l.Id).ToArray();
        if (lotIds.Length == 0) return new BoardPage(LotsMappers.OptionsFor(active), [], []);
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

    /// <summary>The bay's current state as the realtime word ("free", "booked", ...).</summary>
    async Task<string> SpotWordAsync(Guid spotId, CancellationToken ct) =>
        (await db.SpotStateAsync(spotId, ct) ?? SpotState.Free).ToString().ToLowerInvariant();

    static Outcome Error(string message) => Outcome.FlashBack(new Toast("error", message));
}
