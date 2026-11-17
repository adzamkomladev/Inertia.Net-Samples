using System.Linq.Expressions;
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
public sealed class BookingsService(AppDb db, RealtimeEvents realtime, AlertsService alerts, IInertiaCache cache, JobEnqueuer jobs)
{
    const int MaxSlotDays = 90;

    // ---------- windows & pricing ----------

    /// <summary>One slot for a single window; one slot per matching weekday for a recurring pattern. Dates and times of day
    /// are read on the lot's clock (<paramref name="zone"/>) and returned as UTC instants, so a 09:00 weekly window stays
    /// 09:00 at the lot across a daylight-saving change.</summary>
    internal static List<(DateTime Start, DateTime End)> Materialize(BookingInput input, DateTime now, TimeZoneInfo zone)
    {
        if (string.Equals(input.WindowType, "single", StringComparison.OrdinalIgnoreCase))
        {
            DateTime start, end;
            if (input.Date is { Length: > 0 } date)
            {
                if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day)) throw new BookingException("Pick a date.");
                if (!TimeOnly.TryParseExact(input.TimeFrom, "HH:mm", out var from) || !TimeOnly.TryParseExact(input.TimeTo, "HH:mm", out var to))
                    throw new BookingException("Pick a start and end time.");
                if (to <= from) throw new BookingException("The end time must come after the start time.");
                (start, end) = (LotTime.ToUtc(day, from, zone), LotTime.ToUtc(day, to, zone));
            }
            else if (input.StartAtUtc is { } utcStart && input.EndAtUtc is { } utcEnd) (start, end) = (utcStart, utcEnd);
            else throw new BookingException("Pick a start and end time.");
            if (end <= start) throw new BookingException("The end time must come after the start time.");
            if (start < now.AddMinutes(-2)) throw new BookingException("Start times must be in the future.");
            if (start > now.AddDays(MaxSlotDays)) throw new BookingException("Bookings cannot start more than 90 days out.");
            return [(AsUtc(start), AsUtc(end))];
        }

        if (string.Equals(input.WindowType, "recurring", StringComparison.OrdinalIgnoreCase))
        {
            var days = ParseDays(input.DaysOfWeek ?? "");
            if (days.Length == 0) throw new BookingException("Pick at least one weekday.");
            if (!TimeOnly.TryParseExact(input.TimeFrom, "HH:mm", out var from)) throw new BookingException("Choose a start time of day.");
            if (!TimeOnly.TryParseExact(input.TimeTo, "HH:mm", out var to)) throw new BookingException("Choose an end time of day.");
            if (to <= from) throw new BookingException("The end time of each window must come after the start time.");
            var weeks = Math.Clamp(input.Weeks, 1, PricingRules.MaxRecurringWeeks);
            var today = LotTime.Today(now, zone);
            // Weekly windows start tomorrow on the lot's calendar: same-day windows would collide with the check-in grace.
            var slots = Enumerable.Range(1, weeks * 7).Select(today.AddDays).Where(day => days.Contains((int)day.DayOfWeek))
                .Select(day => (LotTime.ToUtc(day, from, zone), LotTime.ToUtc(day, to, zone))).ToList();
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
    async Task<bool> HasConflictAsync(Guid spotId, IReadOnlyCollection<(DateTime Start, DateTime End)> slots, CancellationToken ct, IClientSessionHandle? session = null)
    {
        Expression<Func<Reservation, bool>> holders = r => r.SpotId == spotId && r.Status == ReservationStatus.Confirmed;
        var reservationIds = await (session is null ? db.Reservations.Find(holders) : db.Reservations.Find(session, holders))
            .Project(r => r.Id).ToListAsync(ct);
        if (reservationIds.Count == 0) return false;
        var first = slots.Min(s => s.Start);
        var last = slots.Max(s => s.End);
        Expression<Func<Occurrence, bool>> overlapping = o => reservationIds.Contains(o.ReservationId)
            && (o.Status == OccurrenceStatus.Scheduled || o.Status == OccurrenceStatus.Active)
            && o.StartAtUtc < last && o.EndAtUtc > first;
        var busy = await (session is null ? db.Occurrences.Find(overlapping) : db.Occurrences.Find(session, overlapping))
            .Project(o => new { o.StartAtUtc, o.EndAtUtc }).ToListAsync(ct);
        return busy.Any(b => slots.Any(s => b.StartAtUtc < s.End && b.EndAtUtc > s.Start));
    }

    /// <summary>The lot's bays that a scheduled or active window of a confirmed reservation holds during any of the slots.
    /// Two reads whatever the lot size: the lot's confirmed reservations (lot_status index), then one occurrences query
    /// with $in on their ids plus the time overlap (reservation_index).</summary>
    public async Task<HashSet<Guid>> TakenSpotsAsync(Guid lotId, IReadOnlyCollection<(DateTime Start, DateTime End)> slots, CancellationToken ct)
    {
        if (slots.Count == 0) return [];
        var holders = await db.Reservations.Find(r => r.LotId == lotId && r.Status == ReservationStatus.Confirmed)
            .Project(r => new { r.Id, r.SpotId }).ToListAsync(ct);
        if (holders.Count == 0) return [];
        var spotOf = holders.ToDictionary(h => h.Id, h => h.SpotId);
        var ids = spotOf.Keys.ToArray();
        var first = slots.Min(s => s.Start);
        var last = slots.Max(s => s.End);
        var busy = await db.Occurrences.Find(o => ids.Contains(o.ReservationId)
                && (o.Status == OccurrenceStatus.Scheduled || o.Status == OccurrenceStatus.Active)
                && o.StartAtUtc < last && o.EndAtUtc > first)
            .Project(o => new { o.ReservationId, o.StartAtUtc, o.EndAtUtc }).ToListAsync(ct);
        return busy.Where(b => slots.Any(s => b.StartAtUtc < s.End && b.EndAtUtc > s.Start)).Select(b => spotOf[b.ReservationId]).ToHashSet();
    }

    /// <summary>The booking page's bay map for a chosen window: blocked bays stay blocked, bays held during the window read
    /// "booked", every other bay is "free" for that window whatever is parked on it right now. Null without a valid window.</summary>
    public async Task<SpotView[]?> SpotsForWindowAsync(Lot lot, SpotView[] spots, BookingInput window, CancellationToken ct)
    {
        List<(DateTime Start, DateTime End)> slots;
        try { slots = Materialize(window, DateTime.UtcNow, LotTime.Zone(lot.TimeZoneId)); }
        catch (BookingException) { return null; }
        if (slots.Any(slot => !LotHours.Contains(lot, slot.Start, slot.End))) return null;
        var taken = await TakenSpotsAsync(lot.Id, slots, ct);
        return spots.Select(s => s with { State = s.State == "blocked" ? "blocked" : taken.Contains(s.Id) ? "booked" : "free" }).ToArray();
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
        var lotZones = lots.ToDictionary(l => l.Id, l => l.TimeZoneId);
        string ZoneOf(Guid lotId) => lotZones.GetValueOrDefault(lotId) ?? "UTC";
        // Overstay card (account home): the parked session's window end and the lot's phone. One indexed read, only while parked.
        var sessionWindow = openSession is null ? null
            : await db.Occurrences.Find(o => o.Id == openSession.OccurrenceId).Project(o => new { o.EndAtUtc }).FirstOrDefaultAsync(ct);
        DateTime? sessionWindowEnd = sessionWindow?.EndAtUtc;
        var sessionLotPhone = openSession is null ? null : lots.FirstOrDefault(l => l.Id == openSession.LotId)?.ContactPhone;

        return new AccountData(
            upcoming.Count,
            nextOccurrence is null ? null : new AccountNextVisitRow(nextOccurrence.Id, nextOccurrence.StartAtUtc, nextOccurrence.EndAtUtc,
                spotLabels.GetValueOrDefault(nextReservation!.SpotId), lotNames.GetValueOrDefault(nextReservation.LotId), nextReservation.Id,
                ZoneOf(nextReservation.LotId)),
            shown.Select(r => new AccountUpcomingRow(r.Id, r.TotalCents, r.WindowType.ToString().ToLowerInvariant(), r.StartAtUtc, r.EndAtUtc,
                r.OccurrenceCount, lotNames.GetValueOrDefault(r.LotId), spotLabels.GetValueOrDefault(r.SpotId), ZoneOf(r.LotId))).ToArray(),
            openSession is null ? null : new AccountSessionRow(openSession.Id, openSession.EnteredAtUtc,
                lotNames.GetValueOrDefault(openSession.LotId), spotLabels.GetValueOrDefault(openSession.SpotId), ZoneOf(openSession.LotId),
                sessionWindowEnd, sessionLotPhone),
            paidIn30Days.Sum(),
            await unreadTask,
            LotsMappers.OptionsFor(lots));
    }

    public const int PageSize = 20;

    /// <summary>The customer's bookings split into upcoming and past, one page at a time.
    /// Past: cancelled, completed or ended bookings, latest end first, paged in Mongo (the user_end index serves the sort).
    /// Upcoming: confirmed bookings that have not ended, soonest next visit first. The next visit of a weekly booking lives
    /// on its occurrences, so the order is computed from two narrow projections (the user's still-running reservation ids,
    /// then their unfinished windows) and only the page's reservations are read in full.</summary>
    public async Task<BookingsPage> ListAsync(Guid userId, bool past, PageRequest request, CancellationToken ct)
    {
        request = request with { PerPage = PageSize };
        var now = DateTime.UtcNow;
        if (past)
        {
            var ended = await db.Reservations.Find(r => r.UserId == userId
                    && (r.Status != ReservationStatus.Confirmed || (r.EndAtUtc != null && r.EndAtUtc < now)))
                .SortByDescending(r => r.EndAtUtc).ThenByDescending(r => r.Id)
                .ToPageAsync(request, ct: ct);
            return new BookingsPage(await ViewsAsync(ended.Data, ct), ended.CurrentPage, ended.HasMore);
        }

        var running = await db.Reservations.Find(r => r.UserId == userId && r.Status == ReservationStatus.Confirmed
                && (r.EndAtUtc == null || r.EndAtUtc >= now))
            .Project(r => new { r.Id, r.StartAtUtc }).ToListAsync(ct);
        var runningIds = running.Select(r => r.Id).ToArray();
        var nextStart = runningIds.Length == 0
            ? new Dictionary<Guid, DateTime>()
            : (await db.Occurrences.Find(o => runningIds.Contains(o.ReservationId) && o.EndAtUtc >= now
                    && (o.Status == OccurrenceStatus.Scheduled || o.Status == OccurrenceStatus.Active))
                .Project(o => new { o.ReservationId, o.StartAtUtc }).ToListAsync(ct))
                .GroupBy(o => o.ReservationId).ToDictionary(g => g.Key, g => g.Min(o => o.StartAtUtc));
        var ordered = running
            .OrderBy(r => nextStart.TryGetValue(r.Id, out var next) ? next : r.StartAtUtc ?? DateTime.MaxValue)
            .ThenBy(r => r.Id).Select(r => r.Id).ToList();
        var page = Math.Max(request.Page, 1);
        var pageIds = ordered.Skip((page - 1) * PageSize).Take(PageSize).ToArray();
        var rows = pageIds.Length == 0 ? [] : await db.Reservations.Find(r => pageIds.Contains(r.Id)).ToListAsync(ct);
        var byId = rows.ToDictionary(r => r.Id);
        var inOrder = pageIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
        return new BookingsPage(await ViewsAsync(inOrder, ct), page, ordered.Count > page * PageSize);
    }

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
                    c.Status.ToString().ToLowerInvariant(), c.CreatedAt, c.DecisionNote)).ToArray()),
            CancelPreviewOf(reservation, view.Occurrences, payment, DateTime.UtcNow)), null);
    }

    /// <summary>The refund cancelling now would request, by the rule CancelAsync applies: the next unstarted window sets the
    /// lead time, and only a paid booking gets money back. Null when there is nothing left to cancel.</summary>
    internal static CancelPreview? CancelPreviewOf(Reservation reservation, IEnumerable<OccurrenceView> occurrences, Payment? payment, DateTime now)
    {
        if (reservation.Status != ReservationStatus.Confirmed) return null;
        var scheduled = occurrences.Where(o => o.Status == "scheduled").ToList();
        if (scheduled.Count == 0) return null;
        DateTime? nextStart = scheduled.Where(o => o.StartAtUtc > now).Select(o => (DateTime?)o.StartAtUtc).Min();
        var (rule, cents) = RefundRules.ComputeCents(nextStart, reservation.TotalCents, now);
        var paid = payment is { Status: PaymentStatus.Paid };
        return new CancelPreview(rule, paid ? cents : 0, paid, nextStart);
    }

    /// <summary>The customer's payments, newest first, one page at a time; refunds are those of the payments on the page.</summary>
    public async Task<PaymentsPage> PaymentsAsync(Guid userId, PageRequest request, CancellationToken ct)
    {
        var page = await db.Payments.Find(p => p.UserId == userId).SortByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .ToPageAsync(request with { PerPage = PageSize }, ct: ct);
        var payments = page.Data;
        var reservationIds = payments.Select(p => p.ReservationId).Distinct().ToArray();
        var reservations = (await db.Reservations.Find(r => reservationIds.Contains(r.Id)).Project(r => new { r.Id, r.LotId, r.SpotId }).ToListAsync(ct))
            .ToDictionary(r => r.Id);
        var lotIds = reservations.Values.Select(r => r.LotId).Distinct().ToArray();
        var spotIds = reservations.Values.Select(r => r.SpotId).Distinct().ToArray();
        var lotNames = (await db.Lots.Find(l => lotIds.Contains(l.Id)).Project(l => new { l.Id, l.Name }).ToListAsync(ct)).ToDictionary(l => l.Id, l => l.Name);
        var spotLabels = (await db.Spots.Find(s => spotIds.Contains(s.Id)).Project(s => new { s.Id, s.Label }).ToListAsync(ct)).ToDictionary(s => s.Id, s => s.Label);
        var refunds = await db.Refunds.Find(rf => reservationIds.Contains(rf.ReservationId)).SortByDescending(rf => rf.CreatedAt).ToListAsync(ct);
        // A paid charge whose booking has a cancellation refund on the way reads "refund_pending", and "refunded" once it is
        // paid out, so the list never says "Paid" for money that is coming back. Spot Cover payouts and rejected or
        // zero-amount refunds leave the charge as it is.
        var refundOf = refunds.Where(rf => rf.Rule != "policy_payout" && rf.AmountCents > 0 && rf.Status != RefundStatus.Rejected)
            .GroupBy(rf => rf.ReservationId).ToDictionary(g => g.Key, g => g.First().Status);
        string StatusOf(Payment p) => p.Status == PaymentStatus.Paid && refundOf.TryGetValue(p.ReservationId, out var refund)
            ? refund == RefundStatus.Paid ? "refunded" : "refund_pending"
            : p.Status.ToString().ToLowerInvariant();
        return new PaymentsPage(
            payments.Select(p =>
            {
                var booking = reservations.GetValueOrDefault(p.ReservationId);
                return new CustomerPaymentRow(p.Id, p.ReservationId, p.AmountCents, p.Method.ToString().ToLowerInvariant(),
                    StatusOf(p), p.PaidAt, p.CreatedAt,
                    booking is null ? null : lotNames.GetValueOrDefault(booking.LotId),
                    booking is null ? null : spotLabels.GetValueOrDefault(booking.SpotId));
            }).ToArray(),
            refunds.Select(rf => new CustomerRefundRow(rf.Id, rf.ReservationId, rf.Rule, rf.AmountCents,
                rf.Status.ToString().ToLowerInvariant(), rf.CreatedAt)).ToArray(),
            page.CurrentPage, page.HasMore);
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
        var lots = (await db.Lots.Find(l => lotIds.Contains(l.Id)).Project(l => new { l.Id, l.Name, l.TimeZoneId }).ToListAsync(ct)).ToDictionary(l => l.Id);
        var occurrences = (await db.Occurrences.Find(o => reservationIds.Contains(o.ReservationId)).SortBy(o => o.Index).ToListAsync(ct))
            .ToLookup(o => o.ReservationId);
        var policies = policyIds.Length == 0 ? []
            : (await db.SpotCoverPolicies.Find(p => policyIds.Contains(p.Id)).Project(p => new { p.Id, p.Status }).ToListAsync(ct)).ToDictionary(p => p.Id, p => p.Status);
        return rows.Select(r => new BookingView(
            r.Id, r.LotId, r.SpotId, lots.GetValueOrDefault(r.LotId)?.Name ?? "Lot", spots.GetValueOrDefault(r.SpotId) ?? "?",
            r.WindowType.ToString().ToLowerInvariant(), r.TotalCents, r.ParkingCents, r.InsuranceCents,
            r.Status.ToString().ToLowerInvariant(), r.CreatedAt, r.CancelledAt, r.StartAtUtc, r.EndAtUtc,
            r.Pattern, r.OccurrenceCount, r.PlateNo,
            r.InsuranceId is { } pid && policies.TryGetValue(pid, out var policy) ? policy.ToString().ToLowerInvariant() : null,
            occurrences[r.Id].Select(o => new OccurrenceView(o.Id, o.Index, o.StartAtUtc, o.EndAtUtc, o.Status.ToString().ToLowerInvariant())).ToArray(),
            r.Note, lots.GetValueOrDefault(r.LotId)?.TimeZoneId ?? "UTC")).ToArray();
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
        try { slots = Materialize(input, DateTime.UtcNow, LotTime.Zone(lot.TimeZoneId)); }
        catch (BookingException error) { return Outcome.Fail("window", error.Message); }

        if (slots.Any(slot => !LotHours.Contains(lot, slot.Start, slot.End)))
            return Outcome.Fail("window", $"Choose a window within {lot.Opens}–{lot.Closes} ({LotTime.ZoneName(lot.TimeZoneId)}).");
        // A bay is bookable for any window it is not blocked or already reserved for: a car parked now, or another
        // booking on a different day, does not stop a later reservation.
        if (spot.State is SpotState.Blocked) return Outcome.Fail("spotId", $"Bay {spot.Label} is closed right now. Pick another bay.");
        if (await HasConflictAsync(spotId, slots, ct))
            return Outcome.Fail("spotId", $"Bay {spot.Label} is already reserved for part of that time. Pick another bay.");

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

        // Replica mode serializes bookings of one bay through the transaction itself: its first write bumps the spot's
        // BookingVersion, so a concurrent booking of the same bay hits a write conflict, retries on a fresh snapshot and
        // sees the winner's reservation in the overlap re-check. No timer is involved, so a stalled request cannot lose
        // its place. Single mode has no transactions; there a short per-bay lease is the best-effort serialization (it
        // can expire under a stall, the documented single-mode limit). A free bay is also marked Booked; a bay already
        // holding another (non-overlapping) booking, or with a car parked on it now, keeps its state.
        DateTime? lockUntil = null;
        if (db.Mode == MongoMode.Single)
        {
            var lockNow = DateTime.UtcNow;
            // Whole milliseconds, as Mongo stores them, so the release below matches its own lease exactly.
            lockUntil = DateTime.UnixEpoch.AddMilliseconds(new DateTimeOffset(lockNow.AddSeconds(30)).ToUnixTimeMilliseconds());
            var leased = await db.Spots.UpdateOneAsync(s => s.Id == spotId && s.Active && s.State != SpotState.Blocked
                    && (s.BookingLockUntil == null || s.BookingLockUntil < lockNow),
                Builders<Spot>.Update.Set(s => s.BookingLockUntil, lockUntil), cancellationToken: ct);
            if (leased.ModifiedCount == 0)
                return Outcome.Fail("spotId", $"Bay {spot.Label} was just taken. Pick another bay.");
        }
        try
        {
            bool committed;
            try
            {
                committed = await db.TransactionAsync(async session =>
                {
                    // First write: the bay's version. It is also the snapshot point, so the re-check below sees every
                    // booking committed before it, and any booking committing later conflicts on this document.
                    var claimed = await db.Spots.UpdateOneAsync(session, s => s.Id == spotId && s.Active && s.State != SpotState.Blocked,
                        Builders<Spot>.Update.Inc(s => s.BookingVersion, 1), cancellationToken: ct);
                    if (claimed.MatchedCount == 0 || await HasConflictAsync(spotId, slots, ct, session)) return false;
                    await db.Spots.UpdateOneAsync(session, s => s.Id == spotId && s.State == SpotState.Free,
                        Builders<Spot>.Update.Set(s => s.State, SpotState.Booked), cancellationToken: ct);
                    await db.Reservations.InsertOneAsync(session, reservation, cancellationToken: ct);
                    await db.Occurrences.InsertManyAsync(session, occurrences, cancellationToken: ct);
                    await db.Payments.InsertOneAsync(session, payment, cancellationToken: ct);
                    if (policy is not null) await db.SpotCoverPolicies.InsertOneAsync(session, policy, cancellationToken: ct);
                    return true;
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
            if (!committed)
                return Outcome.Fail("spotId", $"Bay {spot.Label} was just taken for that time. Pick another bay.");
        }
        finally
        {
            if (lockUntil is not null)
                await db.Spots.UpdateOneAsync(s => s.Id == spotId && s.BookingLockUntil == lockUntil,
                    Builders<Spot>.Update.Unset(s => s.BookingLockUntil), cancellationToken: CancellationToken.None);
        }

        await realtime.SpotChangedAsync(lotId, spotId, "booked");
        await AnalyticsCache.LotActivityAsync(cache, lotId, ct);
        // After the commit: the confirmation mail carries ids only and is unique per reservation.
        await jobs.DispatchAsync(new BookingConfirmedEmail(reservation.Id), ct);
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
        await AnalyticsCache.LotActivityAsync(cache, reservation.LotId, ct);
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
        if (await db.HasOpenSessionForReservationAsync(id, ct))
            return Outcome.FlashBack(new Toast("error", "A session is open on this booking. Please check out first."));

        var now = DateTime.UtcNow;
        var nextStart = await db.Occurrences.Find(o => o.ReservationId == id && o.Status == OccurrenceStatus.Scheduled && o.StartAtUtc > now)
            .SortBy(o => o.StartAtUtc).Project(o => o.StartAtUtc).FirstOrDefaultAsync(ct);
        var cancelled = await db.Reservations.UpdateOneAsync(r => r.Id == id && r.Status == ReservationStatus.Confirmed,
            Builders<Reservation>.Update.Set(r => r.Status, ReservationStatus.Cancelled).Set(r => r.CancelledAt, now), cancellationToken: ct);
        if (cancelled.ModifiedCount == 0)
            return Outcome.FlashBack(new Toast("info", "This booking was already cancelled."));

        // Every window not yet used is cancelled, including one that started without a check-in; a Scheduled row left behind
        // would otherwise sit in front of the no-show sweep forever.
        await db.Occurrences.UpdateManyAsync(o => o.ReservationId == id && o.Status == OccurrenceStatus.Scheduled,
            Builders<Occurrence>.Update.Set(o => o.Status, OccurrenceStatus.Cancelled), cancellationToken: ct);
        await FreeSpotIfBookedAsync(reservation.SpotId, ct);
        await realtime.SpotChangedAsync(reservation.LotId, reservation.SpotId,
            (await db.SpotStateAsync(reservation.SpotId, ct) ?? SpotState.Free).ToString().ToLowerInvariant(), ct);

        var payment = await db.Payments.Find(p => p.ReservationId == id && p.Status == PaymentStatus.Paid).FirstOrDefaultAsync(ct);
        var (rule, amount) = RefundRules.ComputeCents(nextStart, reservation.TotalCents, now);
        var refundRequested = payment is not null && amount > 0;
        if (refundRequested)
        {
            await db.Refunds.InsertOneAsync(new Refund
            {
                ReservationId = id, PaymentId = payment!.Id, RequestedByUserId = userId,
                Rule = rule, AmountCents = amount, Reason = "Customer cancelled",
            }, cancellationToken: ct);
            await alerts.NotifyStaffAsync("New refund request", "/admin/refunds", ct);
            await realtime.BoardChangedAsync("refund", ct);
        }

        await alerts.CreateAsync(reservation.UserId, "refund", "Booking cancelled",
            $"Reservation {Display.ShortId(id).ToUpperInvariant()} was cancelled.", "/account/payments", ct: ct);
        await AnalyticsCache.LotActivityAsync(cache, reservation.LotId, ct);
        if (refundRequested) await AnalyticsCache.RefundsChangedAsync(cache, ct);
        return Outcome.FlashBack(new Toast("success", "Booking cancelled."));
    }

    /// <summary>Booked becomes Free only when nothing is parked on the spot and no other confirmed booking still has a window
    /// ahead on it.</summary>
    async Task FreeSpotIfBookedAsync(Guid spotId, CancellationToken ct)
    {
        if (await db.HasOpenSessionAsync(spotId, ct)) return;
        if ((await db.HeldSpotIdsAsync([spotId], DateTime.UtcNow, ct)).Contains(spotId)) return;
        await db.Spots.UpdateOneAsync(s => s.Id == spotId && s.State == SpotState.Booked,
            Builders<Spot>.Update.Set(s => s.State, SpotState.Free).Set(s => s.LastCheckpointAt, DateTime.UtcNow), cancellationToken: ct);
    }
}
