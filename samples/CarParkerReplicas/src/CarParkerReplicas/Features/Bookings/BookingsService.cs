using System.Text.Json.Serialization;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Alerts;
using CarParker.Features.Analytics;
using CarParker.Features.Guard;
using CarParker.Features.Insurance;
using CarParker.Features.Lots;
using CarParker.Infrastructure;
using Inertia.Net;

namespace CarParker.Features.Bookings;

/// <summary>Booking domain: window materialization, pricing, conflict checks and the customer actions. Money is cents.</summary>
public sealed class BookingsService(AppDb db, RealtimeEvents realtime, AlertsService alerts, IInertiaCache cache)
{
    const int MaxSlotDays = 90;

    // ---------- windows & pricing ----------

    /// <summary>One slot for a single window; one slot per matching weekday for a recurring pattern.</summary>
    internal static List<(DateTime Start, DateTime End)> Materialize(BookingInput input, DateTime now)
    {
        if (string.Equals(input.WindowType, "single", StringComparison.OrdinalIgnoreCase))
        {
            if (input.StartAtUtc is not { } start || input.EndAtUtc is not { } end)
                throw new BookingException("Pick a start and end time.");
            if (end <= start) throw new BookingException("The end time must come after the start time.");
            if (start < now.AddMinutes(-2)) throw new BookingException("Start times must be in the future.");
            if (start > now.AddDays(MaxSlotDays)) throw new BookingException("Bookings cannot start more than 90 days out.");
            return [(AsUtc(start), AsUtc(end))];
        }

        if (string.Equals(input.WindowType, "recurring", StringComparison.OrdinalIgnoreCase))
        {
            var days = ParseDays(input.DaysOfWeek!);
            if (days.Length == 0) throw new BookingException("Pick at least one weekday.");
            if (!TimeOnly.TryParseExact(input.TimeFrom, "HH:mm", out var from)) throw new BookingException("Choose a start time of day.");
            if (!TimeOnly.TryParseExact(input.TimeTo, "HH:mm", out var to)) throw new BookingException("Choose an end time of day.");
            if (to <= from) throw new BookingException("The end time of each window must come after the start time.");
            var weeks = Math.Clamp(input.Weeks, 1, PricingRules.MaxRecurringWeeks);
            var today = DateOnly.FromDateTime(now);
            // Weekly windows start tomorrow: same-day windows would collide with the check-in grace.
            var slots = Enumerable.Range(1, weeks * 7).Select(today.AddDays).Where(day => days.Contains((int)day.DayOfWeek))
                .Select(day => (day.ToDateTime(from, DateTimeKind.Utc), day.ToDateTime(to, DateTimeKind.Utc))).ToList();
            return slots.Count > 0 ? slots : throw new BookingException("No weekly window matched the picked days in the coming eight weeks.");
        }

        throw new BookingException("Choose a single window or a recurring weekly pattern.");
    }

    static DateTime AsUtc(DateTime value) => value.Kind is DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    static int[] ParseDays(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var n) && n is >= 0 and <= 6 ? n : -1).Where(n => n >= 0).Distinct().ToArray();

    static PaymentMethod? ParseMethod(string? value) => value?.ToLowerInvariant() switch
    {
        "card" => PaymentMethod.Card,
        "mobile_money" => PaymentMethod.MobileMoney,
        "pay_at_gate" => PaymentMethod.PayAtGate,
        _ => null,
    };

    /// <summary>True when a scheduled or active window of a confirmed reservation on the spot overlaps any slot (one query for all slots).</summary>
    async Task<bool> HasConflictAsync(Guid spotId, IReadOnlyCollection<(DateTime Start, DateTime End)> slots, CancellationToken ct)
    {
        var reservationIds = await db.Reservations.Distinct(r => r.Id, r => r.SpotId == spotId && r.Status == ReservationStatus.Confirmed).ToListAsync(ct);
        if (reservationIds.Count == 0) return false;
        var first = slots.Min(s => s.Start);
        var last = slots.Max(s => s.End);
        var busy = await db.Occurrences.Find(o => reservationIds.Contains(o.ReservationId)
                && (o.Status == OccurrenceStatus.Scheduled || o.Status == OccurrenceStatus.Active)
                && o.StartAtUtc < last && o.EndAtUtc > first)
            .Project(o => new { o.StartAtUtc, o.EndAtUtc }).ToListAsync(ct);
        return busy.Any(b => slots.Any(s => b.StartAtUtc < s.End && b.EndAtUtc > s.Start));
    }

    // ---------- customer pages ----------

    public async Task<AccountData> AccountAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // Stage 1: the reservation list every later lookup is built from.
        var confirmed = await db.Reservations.Find(r => r.UserId == userId && r.Status == ReservationStatus.Confirmed)
            .SortByDescending(r => r.CreatedAt).ToListAsync(ct);
        var upcoming = confirmed.Where(r => r.EndAtUtc is not { } end || end >= now).ToList();
        var upcomingIds = upcoming.Select(r => r.Id).ToArray();
        var confirmedIds = confirmed.Select(r => r.Id).ToArray();

        // Stage 2: independent lookups run together. Empty id lists never become an $in: [] scan.
        async Task<Occurrence?> NextOccurrenceAsync()
        {
            if (upcomingIds.Length == 0) return null;
            return await db.Occurrences.Find(o => upcomingIds.Contains(o.ReservationId) && o.Status == OccurrenceStatus.Scheduled && o.StartAtUtc > now)
                .SortBy(o => o.StartAtUtc).FirstOrDefaultAsync(ct);
        }

        var nextOccurrenceTask = NextOccurrenceAsync();
        var occurrenceIdsTask = confirmedIds.Length == 0
            ? Task.FromResult(new List<Guid>())
            : db.Occurrences.Distinct(o => o.Id, o => confirmedIds.Contains(o.ReservationId)).ToListAsync(ct);
        var paidIn30DaysTask = db.Payments.Find(p => p.UserId == userId && p.Status == PaymentStatus.Paid && p.PaidAt >= now.AddDays(-30))
            .Project(p => p.AmountCents).ToListAsync(ct);
        var allLotIdsTask = db.Reservations.Distinct(r => r.LotId, r => r.UserId == userId).ToListAsync(ct);
        var unreadTask = alerts.CountUnreadAsync(userId, ct);
        await Task.WhenAll(nextOccurrenceTask, occurrenceIdsTask, paidIn30DaysTask, allLotIdsTask, unreadTask);

        var nextOccurrence = await nextOccurrenceTask;
        var occurrenceIds = await occurrenceIdsTask;
        var paidIn30Days = await paidIn30DaysTask;
        var allLotIds = await allLotIdsTask;
        var shown = upcoming.Take(4).ToList();
        var nextReservation = nextOccurrence is null ? null : upcoming.First(r => r.Id == nextOccurrence.ReservationId);

        // Stage 3: lookups that depend on stage 2, still in parallel with each other.
        async Task<ParkingSession?> OpenSessionAsync()
        {
            if (occurrenceIds.Count == 0) return null;
            return await db.ParkingSessions.Find(s => s.Status == SessionStatus.InProgress && occurrenceIds.Contains(s.OccurrenceId))
                .SortByDescending(s => s.EnteredAtUtc).FirstOrDefaultAsync(ct);
        }

        var openSessionTask = OpenSessionAsync();
        var lotsTask = allLotIds.Count == 0
            ? Task.FromResult(new List<Lot>())
            : db.Lots.Find(l => allLotIds.Contains(l.Id)).SortBy(l => l.Name).ToListAsync(ct);
        await Task.WhenAll(openSessionTask, lotsTask);
        var openSession = await openSessionTask;
        var lots = await lotsTask;

        var spotIds = shown.Select(r => r.SpotId).Concat(nextReservation is null ? [] : [nextReservation.SpotId])
            .Concat(openSession is null ? [] : [openSession.SpotId]).Distinct().ToArray();
        var spotLabels = spotIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await db.Spots.Find(s => spotIds.Contains(s.Id)).Project(s => new { s.Id, s.Label }).ToListAsync(ct)).ToDictionary(s => s.Id, s => s.Label);
        var lotNames = lots.ToDictionary(l => l.Id, l => l.Name);

        return new AccountData(
            upcoming.Count,
            nextOccurrence is null ? null : new AccountNextVisitRow(nextOccurrence.Id, nextOccurrence.StartAtUtc, nextOccurrence.EndAtUtc,
                spotLabels.GetValueOrDefault(nextReservation!.SpotId), lotNames.GetValueOrDefault(nextReservation.LotId), nextReservation.Id),
            shown.Select(r => new AccountUpcomingRow(r.Id, r.TotalCents, r.WindowType.ToString().ToLowerInvariant(), r.StartAtUtc, r.EndAtUtc,
                r.OccurrenceCount, lotNames.GetValueOrDefault(r.LotId), spotLabels.GetValueOrDefault(r.SpotId))).ToArray(),
            openSession is null ? null : new AccountSessionRow(openSession.Id, openSession.EnteredAtUtc,
                lotNames.GetValueOrDefault(openSession.LotId), spotLabels.GetValueOrDefault(openSession.SpotId)),
            paidIn30Days.Sum(),
            await unreadTask,
            LotsMappers.OptionsFor(lots));
    }

    public async Task<BookingView[]> ListAsync(Guid userId, CancellationToken ct) =>
        await ViewsAsync(await db.Reservations.Find(r => r.UserId == userId).SortByDescending(r => r.CreatedAt).ToListAsync(ct), ct);

    /// <summary>One booking with payment, refund, sessions and cover. Null when missing; Forbidden when it belongs to someone else.</summary>
    public async Task<(BookingDetail? Detail, Outcome? Failure)> DetailAsync(Guid id, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var reservation = await db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (reservation is null) return (null, Outcome.Missing());
        if (reservation.UserId != userId && !isAdmin) return (null, Outcome.Denied());

        var view = (await ViewsAsync([reservation], ct))[0];
        var occurrenceIds = view.Occurrences.Select(o => o.Id).ToArray();
        var paymentTask = db.Payments.Find(p => p.ReservationId == id).SortByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);
        var refundTask = db.Refunds.Find(rf => rf.ReservationId == id).SortByDescending(rf => rf.CreatedAt).FirstOrDefaultAsync(ct);
        var sessionsTask = db.ParkingSessions.Find(s => occurrenceIds.Contains(s.OccurrenceId)).ToListAsync(ct);
        var policyTask = reservation.InsuranceId is { } policyId
            ? db.SpotCoverPolicies.Find(p => p.Id == policyId).FirstOrDefaultAsync(ct)!
            : Task.FromResult<SpotCoverPolicy?>(null)!;
        await Task.WhenAll(paymentTask, refundTask, sessionsTask, policyTask);
        var (payment, refund, policy) = (paymentTask.Result, refundTask.Result, policyTask.Result);
        var claims = policy is null ? [] : await db.PolicyClaims.Find(c => c.PolicyId == policy.Id).SortByDescending(c => c.CreatedAt).ToListAsync(ct);

        return (new BookingDetail(
            view,
            payment is null ? null : new BookingPaymentRow(payment.Id, payment.Method.ToString().ToLowerInvariant(), payment.AmountCents,
                payment.Status.ToString().ToLowerInvariant(), payment.PaidAt),
            refund is null ? null : new BookingRefundRow(refund.Id, refund.Rule, refund.AmountCents,
                refund.Status.ToString().ToLowerInvariant(), refund.CreatedAt),
            sessionsTask.Result.Select(s => new BookingSessionRow(s.Id, s.SpotId, s.PlateNo, s.EnteredAtUtc, s.ExitedAtUtc,
                s.Status.ToString().ToLowerInvariant())).ToArray(),
            policy is null ? null : new BookingPolicyRow(policy.Id, policy.PremiumCents, policy.CoverageCents,
                policy.Status.ToString().ToLowerInvariant(),
                claims.Select(c => new BookingClaimRow(c.Id, c.Kind.ToString().ToLowerInvariant(), c.ClaimedCents, c.PayoutCents,
                    c.Status.ToString().ToLowerInvariant(), c.CreatedAt, c.DecisionNote)).ToArray())), null);
    }

    public async Task<PaymentsPage> PaymentsAsync(Guid userId, CancellationToken ct)
    {
        var payments = await db.Payments.Find(p => p.UserId == userId).SortByDescending(p => p.CreatedAt).ToListAsync(ct);
        var reservationIds = payments.Select(p => p.ReservationId).Distinct().ToArray();
        var reservations = (await db.Reservations.Find(r => reservationIds.Contains(r.Id)).Project(r => new { r.Id, r.LotId, r.SpotId }).ToListAsync(ct))
            .ToDictionary(r => r.Id);
        var lotIds = reservations.Values.Select(r => r.LotId).Distinct().ToArray();
        var spotIds = reservations.Values.Select(r => r.SpotId).Distinct().ToArray();
        var lotNames = (await db.Lots.Find(l => lotIds.Contains(l.Id)).Project(l => new { l.Id, l.Name }).ToListAsync(ct)).ToDictionary(l => l.Id, l => l.Name);
        var spotLabels = (await db.Spots.Find(s => spotIds.Contains(s.Id)).Project(s => new { s.Id, s.Label }).ToListAsync(ct)).ToDictionary(s => s.Id, s => s.Label);
        var refunds = await db.Refunds.Find(rf => reservationIds.Contains(rf.ReservationId)).SortByDescending(rf => rf.CreatedAt).ToListAsync(ct);
        return new PaymentsPage(
            payments.Select(p =>
            {
                var booking = reservations.GetValueOrDefault(p.ReservationId);
                return new CustomerPaymentRow(p.Id, p.ReservationId, p.AmountCents, p.Method.ToString().ToLowerInvariant(),
                    p.Status.ToString().ToLowerInvariant(), p.PaidAt, p.CreatedAt,
                    booking is null ? null : lotNames.GetValueOrDefault(booking.LotId),
                    booking is null ? null : spotLabels.GetValueOrDefault(booking.SpotId));
            }).ToArray(),
            refunds.Select(rf => new CustomerRefundRow(rf.Id, rf.ReservationId, rf.Rule, rf.AmountCents,
                rf.Status.ToString().ToLowerInvariant(), rf.CreatedAt)).ToArray());
    }

    /// <summary>Booking rows with lot and spot labels, cover status and per-window statuses, in four batched reads.</summary>
    public async Task<BookingView[]> ViewsAsync(IReadOnlyCollection<Reservation> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var spotIds = rows.Select(r => r.SpotId).Distinct().ToArray();
        var lotIds = rows.Select(r => r.LotId).Distinct().ToArray();
        var reservationIds = rows.Select(r => r.Id).ToArray();
        var policyIds = rows.Where(r => r.InsuranceId is not null).Select(r => r.InsuranceId!.Value).ToArray();
        var spots = (await db.Spots.Find(s => spotIds.Contains(s.Id)).Project(s => new { s.Id, s.Label }).ToListAsync(ct)).ToDictionary(s => s.Id, s => s.Label);
        var lots = (await db.Lots.Find(l => lotIds.Contains(l.Id)).Project(l => new { l.Id, l.Name }).ToListAsync(ct)).ToDictionary(l => l.Id, l => l.Name);
        var occurrences = (await db.Occurrences.Find(o => reservationIds.Contains(o.ReservationId)).SortBy(o => o.Index).ToListAsync(ct))
            .ToLookup(o => o.ReservationId);
        var policies = policyIds.Length == 0 ? []
            : (await db.SpotCoverPolicies.Find(p => policyIds.Contains(p.Id)).Project(p => new { p.Id, p.Status }).ToListAsync(ct)).ToDictionary(p => p.Id, p => p.Status);
        return rows.Select(r => new BookingView(
            r.Id, r.LotId, r.SpotId, lots.GetValueOrDefault(r.LotId) ?? "Lot", spots.GetValueOrDefault(r.SpotId) ?? "?",
            r.WindowType.ToString().ToLowerInvariant(), r.TotalCents, r.ParkingCents, r.InsuranceCents,
            r.Status.ToString().ToLowerInvariant(), r.CreatedAt, r.CancelledAt, r.StartAtUtc, r.EndAtUtc,
            r.Pattern, r.OccurrenceCount, r.PlateNo,
            r.InsuranceId is { } pid && policies.TryGetValue(pid, out var policy) ? policy.ToString().ToLowerInvariant() : null,
            occurrences[r.Id].Select(o => new OccurrenceView(o.Id, o.Index, o.StartAtUtc, o.EndAtUtc, o.Status.ToString().ToLowerInvariant())).ToArray(),
            r.Note)).ToArray();
    }

    // ---------- create ----------

    public async Task<Outcome> CreateAsync(BookingInput input, Guid userId, CancellationToken ct)
    {
        if (input.LotId is not { } lotId || input.SpotId is not { } spotId)
            return Outcome.Fail("spotId", "Pick a spot.");
        var attemptId = input.AttemptId is { Length: > 0 } ? input.AttemptId : null;

        // Wizard retries carry the same attempt id: return the booking the first attempt created.
        if (attemptId is not null && await FindAttemptAsync(userId, attemptId, ct) is { } existing)
            return Outcome.RedirectTo($"/account/bookings/{existing}");

        var lot = await db.Lots.Find(l => l.Id == lotId && l.IsActive).FirstOrDefaultAsync(ct);
        if (lot is null) return Outcome.Fail("lotId", "That lot is not open for bookings.");
        var spot = await db.Spots.Find(s => s.Id == spotId && s.LotId == lotId && s.Active).FirstOrDefaultAsync(ct);
        if (spot is null) return Outcome.Fail("spotId", "That spot does not exist.");
        if (ParseMethod(input.Method) is not { } method) return Outcome.Fail("method", "Choose a payment method.");

        List<(DateTime Start, DateTime End)> slots;
        try { slots = Materialize(input, DateTime.UtcNow); }
        catch (BookingException error) { return Outcome.Fail("window", error.Message); }

        if (slots.Any(slot => !LotHours.Contains(lot, slot.Start, slot.End)))
            return Outcome.Fail("window", $"Choose a window within {lot.Opens}–{lot.Closes} ({lot.TimeZoneId}).");
        if (spot.State is not SpotState.Free) return Outcome.Fail("spotId", $"Spot {spot.Label} is no longer free.");
        if (await HasConflictAsync(spotId, slots, ct))
            return Outcome.Fail("spotId", "That spot is already reserved for part of the chosen window.");

        var parking = slots.Sum(slot => PricingRules.WindowCents(lot.RatePerHourCents, lot.DailyMaxCents, slot.Start, slot.End));
        var premium = input.Insurance ? PricingRules.InsurancePremiumCents(parking) : 0;
        var recurring = input.WindowType?.Equals("recurring", StringComparison.OrdinalIgnoreCase) == true;
        var policy = input.Insurance
            ? new SpotCoverPolicy { UserId = userId, LotId = lotId, PremiumCents = premium, CoverageCents = lot.CoverageCents, StartedAt = slots[0].Start }
            : null;
        var reservation = new Reservation
        {
            UserId = userId,
            LotId = lotId,
            SpotId = spotId,
            WindowType = recurring ? WindowType.Recurring : WindowType.Single,
            StartAtUtc = slots[0].Start,
            EndAtUtc = slots[^1].End,
            Pattern = recurring ? new RecurrencePattern(ParseDays(input.DaysOfWeek!), input.TimeFrom!, input.TimeTo!, Math.Clamp(input.Weeks, 1, PricingRules.MaxRecurringWeeks)) : null,
            OccurrenceCount = slots.Count,
            PlateNo = input.PlateNo is { Length: > 0 } plate ? plate.ToUpperInvariant() : null,
            TotalCents = parking + premium,
            ParkingCents = parking,
            InsuranceCents = premium,
            AttemptId = attemptId,
            InsuranceId = policy?.Id,
            Note = input.Note is { Length: > 0 } ? input.Note : null,
        };
        policy?.ReservationId = reservation.Id;
        var occurrences = slots.Select((slot, index) => new Occurrence
            { ReservationId = reservation.Id, Index = index, StartAtUtc = slot.Start, EndAtUtc = slot.End }).ToList();
        var payment = new Payment { ReservationId = reservation.Id, UserId = userId, Method = method, AmountCents = reservation.TotalCents };

        // Claim the spot outside the transaction so a double booking collides on this conditional update, not inside the transaction.
        var claim = await db.Spots.UpdateOneAsync(s => s.Id == spotId && s.State == SpotState.Free && s.Active,
            Builders<Spot>.Update.Set(s => s.State, SpotState.Booked), cancellationToken: ct);
        if (claim.ModifiedCount == 0)
            return Outcome.Fail("spotId", $"Spot {spot.Label} was just taken. Pick another spot.");

        try
        {
            await db.TransactionAsync(async session =>
            {
                await db.Reservations.InsertOneAsync(session, reservation, cancellationToken: ct);
                await db.Occurrences.InsertManyAsync(session, occurrences, cancellationToken: ct);
                await db.Payments.InsertOneAsync(session, payment, cancellationToken: ct);
                if (policy is not null) await db.SpotCoverPolicies.InsertOneAsync(session, policy, cancellationToken: ct);
            }, ct);
        }
        catch (MongoException error) when (attemptId is not null && error.Message.Contains("user_attempt_unique"))
        {
            // A concurrent same-attempt insert won the unique index: release our claim and show the winner.
            await FreeSpotIfBookedAsync(spotId, ct);
            return Outcome.RedirectTo($"/account/bookings/{await FindAttemptAsync(userId, attemptId, ct)}");
        }
        catch
        {
            // A single-mode failure may leave the reservation saved; keep its bay claimed to prevent double booking.
            if (db.Mode == MongoMode.Replica || !await db.Reservations.Find(r => r.Id == reservation.Id).AnyAsync(CancellationToken.None))
                await FreeSpotIfBookedAsync(spotId, CancellationToken.None);
            throw;
        }

        await realtime.SpotChangedAsync(lotId, spotId, "booked");
        await cache.RemoveByTagAsync(AnalyticsCache.Tag, ct);
        return Outcome.RedirectTo($"/account/bookings/{reservation.Id}");
    }

    async Task<Guid?> FindAttemptAsync(Guid userId, string attemptId, CancellationToken ct) =>
        await db.Reservations.Find(r => r.UserId == userId && r.AttemptId == attemptId).Project(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);

    // ---------- pay & cancel ----------

    /// <summary>Mocked instant gateway: card and mobile money succeed on the spot; pay_at_gate is billed at check-in.</summary>
    public async Task<Outcome> PayAsync(Guid id, PayInput input, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var reservation = await db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (reservation is null) return Outcome.Missing();
        if (reservation.UserId != userId && !isAdmin) return Outcome.Denied();
        if (ParseMethod(input.Method) is not { } method || method == PaymentMethod.PayAtGate)
            return Outcome.Fail("method", "Choose to pay by card or mobile money here; pay at the gate on arrival.");
        if (reservation.Status != ReservationStatus.Confirmed)
            return Outcome.Fail("method", "This booking is no longer active.");

        var payment = await db.Payments.Find(p => p.ReservationId == id && p.Status == PaymentStatus.Pending).FirstOrDefaultAsync(ct);
        if (payment is null) return Outcome.FlashBack(new Toast("success", "This booking is already paid."));
        await db.Payments.UpdateOneAsync(p => p.Id == payment.Id && p.Status == PaymentStatus.Pending,
            Builders<Payment>.Update.Set(p => p.Method, method).Set(p => p.Status, PaymentStatus.Paid)
                .Set(p => p.TxRef, $"demo-{payment.Id:N}").Set(p => p.PaidAt, DateTime.UtcNow),
            cancellationToken: ct);
        await cache.RemoveByTagAsync(AnalyticsCache.Tag, ct);
        return Outcome.FlashBack(new Toast("success", "Payment received."));
    }

    /// <summary>Cancels future windows, frees the spot and requests a refund when the cancellation rules award one.</summary>
    public async Task<Outcome> CancelAsync(Guid id, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var reservation = await db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (reservation is null) return Outcome.Missing();
        if (reservation.UserId != userId && !isAdmin) return Outcome.Denied();
        if (reservation.Status != ReservationStatus.Confirmed)
            return Outcome.FlashBack(new Toast("error", "This booking is not active."));
        if (await db.HasOpenSessionAsync(reservation.SpotId, ct))
            return Outcome.FlashBack(new Toast("error", "A session is open on this spot. Please check out first."));

        var now = DateTime.UtcNow;
        var nextStart = await db.Occurrences.Find(o => o.ReservationId == id && o.Status == OccurrenceStatus.Scheduled && o.StartAtUtc > now)
            .SortBy(o => o.StartAtUtc).Project(o => o.StartAtUtc).FirstOrDefaultAsync(ct);
        var cancelled = await db.Reservations.UpdateOneAsync(r => r.Id == id && r.Status == ReservationStatus.Confirmed,
            Builders<Reservation>.Update.Set(r => r.Status, ReservationStatus.Cancelled).Set(r => r.CancelledAt, now), cancellationToken: ct);
        if (cancelled.ModifiedCount == 0)
            return Outcome.FlashBack(new Toast("info", "This booking was already cancelled."));

        await db.Occurrences.UpdateManyAsync(o => o.ReservationId == id && o.Status == OccurrenceStatus.Scheduled && o.StartAtUtc > now,
            Builders<Occurrence>.Update.Set(o => o.Status, OccurrenceStatus.Cancelled), cancellationToken: ct);
        await FreeSpotIfBookedAsync(reservation.SpotId, ct);
        await realtime.SpotChangedAsync(reservation.LotId, reservation.SpotId, "free", ct);

        var payment = await db.Payments.Find(p => p.ReservationId == id && p.Status == PaymentStatus.Paid).FirstOrDefaultAsync(ct);
        var (rule, amount) = RefundRules.ComputeCents(nextStart, reservation.TotalCents, now);
        if (payment is not null && amount > 0)
        {
            await db.Refunds.InsertOneAsync(new Refund
            {
                ReservationId = id, PaymentId = payment.Id, RequestedByUserId = userId,
                Rule = rule, AmountCents = amount, Reason = "Customer cancelled",
            }, cancellationToken: ct);
            await alerts.NotifyStaffAsync("New refund request", "/admin/refunds", ct);
            await realtime.BoardChangedAsync("refund", ct);
        }

        await alerts.CreateAsync(reservation.UserId, "refund", "Booking cancelled",
            $"Reservation {id.ToString()[..8].ToUpperInvariant()} was cancelled.", "/account/payments", ct: ct);
        await cache.RemoveByTagAsync(AnalyticsCache.Tag, ct);
        return Outcome.FlashBack(new Toast("success", "Booking cancelled."));
    }

    /// <summary>Booked becomes Free only when nothing is parked on the spot.</summary>
    async Task FreeSpotIfBookedAsync(Guid spotId, CancellationToken ct)
    {
        if (await db.HasOpenSessionAsync(spotId, ct)) return;
        await db.Spots.UpdateOneAsync(s => s.Id == spotId && s.State == SpotState.Booked,
            Builders<Spot>.Update.Set(s => s.State, SpotState.Free).Set(s => s.LastCheckpointAt, DateTime.UtcNow), cancellationToken: ct);
    }
}
