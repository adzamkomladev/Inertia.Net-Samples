using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Infrastructure;
using SvelteMongoBlog.Features.Alerts;
using SvelteMongoBlog.Features.Guard;
using SvelteMongoBlog.Features.Insurance;
using SvelteMongoBlog.Features.Lots;

namespace SvelteMongoBlog.Features.Bookings;

/// <summary>The booking domain heart: window materialization, pricing, conflict checks, conditional spot updates,
/// and the customer actions (create/pay/cancel). Cents-long money; "$" is applied on the client only.</summary>
public static class BookingsService
{
    public const int MaxSlotDays = 90; // single windows may not start later than 90 days out

    // ---------- window materialization & pricing ----------

    /// <summary>Materializes the reserved windows: one slot for single-window bookings; per matching day of week for recurring.</summary>
    public static List<(DateTime Start, DateTime End)> Materialize(BookingInput input, DateTime now)
    {
        var slots = new List<(DateTime, DateTime)>();
        if (string.Equals(input.WindowType, "single", StringComparison.OrdinalIgnoreCase))
        {
            if (input.StartAtUtc is not { } start || input.EndAtUtc is not { } end)
                throw new BookingException("Pick a start and end time.");
            if (end <= start) throw new BookingException("The end time must come after the start time.");
            if (start < DateTime.UtcNow.AddMinutes(-2)) throw new BookingException("Start times must be in the future.");
            if (start > DateTime.UtcNow.AddDays(MaxSlotDays)) throw new BookingException("Bookings cannot start more than 90 days out.");
            slots.Add((AsUtc(start), AsUtc(end)));
            return slots;
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
            // Weekly windows start from tomorrow: same-day recurring windows would collide with guard check-in grace.
            for (var offset = 1; offset <= weeks * 7; offset++)
            {
                var day = today.AddDays(offset);
                if (!days.Contains((int)day.DayOfWeek)) continue;
                slots.Add((day.ToDateTime(from, DateTimeKind.Utc), day.ToDateTime(to, DateTimeKind.Utc)));
            }
            if (slots.Count == 0) throw new BookingException("No weekly window matched the picked days in the coming eight weeks.");
            return slots;
        }

        throw new BookingException("Choose a single window or a recurring weekly pattern.");
    }

    static DateTime AsUtc(DateTime value) => value.Kind is DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    static int[] ParseDays(string raw)
    {
        var days = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var n) && n is >= 0 and <= 6 ? n : -1)
            .Where(n => n >= 0).Distinct().ToArray();
        return days;
    }

    public static long ParkingCents(Lot lot, IReadOnlyCollection<(DateTime Start, DateTime End)> slots) =>
        slots.Sum(slot => PricingRules.WindowCents(lot.RatePerHourCents, lot.DailyMaxCents, slot.Start, slot.End));

    /// <summary>12% of the parking total, minimum $2 — charged only when the customer adds Spot Cover.</summary>
    public static long PremiumCents(long parkingCents) => PricingRules.InsurancePremiumCents(parkingCents);

    // ---------- availability ----------

    /// <summary>A slot is bookable when the spot is free and no scheduled/active occurrence of a confirmed
    /// reservation overlaps [start,end). Used by unit tests, create, and the wizard preview.</summary>
    public static async Task<bool> IsFreeAsync(AppDb db, Guid spotId, DateTime start, DateTime end, CancellationToken ct)
    {
        var reservationIds = await db.Reservations.Find(r => r.SpotId == spotId && r.Status == ReservationStatus.Confirmed)
            .Project(r => r.Id).ToListAsync(ct);
        if (reservationIds.Count == 0) return true;
        return !await db.Occurrences.Find(o => reservationIds.Contains(o.ReservationId)
                && (o.Status == OccurrenceStatus.Scheduled || o.Status == OccurrenceStatus.Active)
                && o.StartAtUtc < end && o.EndAtUtc > start)
            .AnyAsync(ct);
    }

    static async Task<(Guid? ConflictStart, string Message)> FindConflictAsync(
        AppDb db, Guid spotId, IReadOnlyCollection<(DateTime Start, DateTime End)> slots, CancellationToken ct)
    {
        var reservationIds = await db.Reservations.Find(r => r.SpotId == spotId && r.Status == ReservationStatus.Confirmed)
            .Project(r => r.Id).ToListAsync(ct);
        foreach (var (start, end) in slots)
        {
            var conflict = await db.Occurrences.Find(o => reservationIds.Contains(o.ReservationId)
                    && (o.Status == OccurrenceStatus.Scheduled || o.Status == OccurrenceStatus.Active)
                    && o.StartAtUtc < end && o.EndAtUtc > start)
                .Project(o => (Guid?)o.Id)
                .FirstOrDefaultAsync(ct);
            if (conflict is not null)
                return (conflict, "That spot is already reserved for part of the chosen window.");
        }
        return (null, "");
    }

    // ---------- create ----------

    public static async Task<Outcome> CreateAsync(
        BookingInput input, Guid userId, AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        var attemptId = input.AttemptId;
        if (input.LotId is not { } lotId || input.SpotId is not { } spotId)
            return Outcome.Fail("spotId", "Pick a spot.");

        // Wizard retries carry the same attempt id: return the booking the first attempt created.
        if (attemptId is { Length: > 0 })
        {
            var existing = await db.Reservations.Find(r => r.UserId == userId && r.AttemptId == attemptId).FirstOrDefaultAsync(ct);
            if (existing is not null)
                return Outcome.RedirectTo($"/account/bookings/{existing.Id}");
        }

        var lot = await db.Lots.Find(l => l.Id == lotId && l.IsActive).FirstOrDefaultAsync(ct);
        if (lot is null)
            return Outcome.Fail("lotId", "That lot is not open for bookings.");
        var spot = await db.Spots.Find(s => s.Id == spotId && s.LotId == lotId && s.Active).FirstOrDefaultAsync(ct);
        if (spot is null)
            return Outcome.Fail("spotId", "That spot does not exist.");

        var method = ParseMethod(input.Method!);
        if (method is null)
            return Outcome.Fail("method", "Choose a payment method.");

        List<(DateTime, DateTime)> slots;
        try { slots = Materialize(input, DateTime.UtcNow); }
        catch (BookingException error)
        {
            return Outcome.Fail("window", error.Message);
        }

        if (spot.State is not SpotState.Free)
            return Outcome.Fail("spotId", $"Spot {spot.Label} is no longer free.");

        var (conflict, message) = await FindConflictAsync(db, spotId, slots, ct);
        if (conflict is not null)
            return Outcome.Fail("spotId", message);

        var parking = ParkingCents(lot, slots);
        var premium = input.Insurance ? PremiumCents(parking) : 0;
        var total = parking + premium;

        var reservationId = Guid.CreateVersion7();
        RecurrencePattern? pattern = null;
        if (input.WindowType?.Equals("recurring", StringComparison.OrdinalIgnoreCase) == true)
            pattern = new RecurrencePattern(ParseDays(input.DaysOfWeek!), input.TimeFrom!, input.TimeTo!, Math.Clamp(input.Weeks, 1, PricingRules.MaxRecurringWeeks));
        var reservation = new Reservation
        {
            Id = reservationId,
            UserId = userId,
            LotId = lotId,
            SpotId = spotId,
            WindowType = pattern is null ? WindowType.Single : WindowType.Recurring,
            StartAtUtc = slots[0].Item1,
            EndAtUtc = slots[^1].Item2,
            Pattern = pattern,
            OccurrenceCount = slots.Count,
            PlateNo = input.PlateNo is { Length: > 0 } plate ? plate.ToUpperInvariant() : null,
            TotalCents = total,
            ParkingCents = parking,
            InsuranceCents = premium,
            Status = ReservationStatus.Confirmed,
            AttemptId = input.AttemptId is { Length: > 0 } ? input.AttemptId : null,
            Note = input.Note is { Length: > 0 } ? input.Note : null,
        };
        var occurrences = slots.Select((slot, index) => new Occurrence
        {
            ReservationId = reservationId,
            Index = index,
            StartAtUtc = slot.Item1,
            EndAtUtc = slot.Item2,
        }).ToList();

        SpotCoverPolicy? policy = null;
        if (input.Insurance)
        {
            policy = new SpotCoverPolicy
            {
                ReservationId = reservationId,
                UserId = userId,
                LotId = lotId,
                PremiumCents = premium,
                CoverageCents = lot.CoverageCents,
                StartedAt = slots[0].Item1,
            };
        }

        var payment = new Payment
        {
            ReservationId = reservationId,
            UserId = userId,
            Method = method.Value,
            AmountCents = total,
        };

        // Claim the spot outside the transaction so double bookings collide on a conditional update (409), never inside tx.
        var claim = await db.Spots.UpdateOneAsync(
            s => s.Id == spotId && s.State == SpotState.Free && s.Active,
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
                if (policy is not null)
                {
                    reservation.InsuranceId = policy.Id; // the policy row carries the id back onto the reservation
                    await db.SpotCoverPolicies.InsertOneAsync(session, policy, cancellationToken: ct);
                    await db.Reservations.UpdateOneAsync(session, r => r.Id == reservationId,
                        Builders<Reservation>.Update.Set(r => r.InsuranceId, policy.Id), cancellationToken: ct);
                }
            }, ct);
        }
        catch (MongoWriteException error) when (error.WriteError.Code == 11000 && error.Message.Contains("user_attempt_unique"))
        {
            // A concurrent same-attempt insert won the unique index: release our claim and show the winner.
            await ReleaseClaimedSpotAsync(db, spotId, ct);
            var winner = await db.Reservations.Find(r => r.UserId == userId && r.AttemptId == attemptId).FirstOrDefaultAsync(ct);
            return Outcome.RedirectTo($"/account/bookings/{winner!.Id}");
        }
        catch (Exception)
        {
            await ReleaseClaimedSpotAsync(db, spotId, ct);
            throw;
        }

        await realtime.SpotChangedAsync(lotId, spotId, "booked");
        return Outcome.RedirectTo($"/account/bookings/{reservationId}");
    }

    /// <summary>Releases the spot claimed before the transaction; never touches a spot with an open session.</summary>
    static async Task ReleaseClaimedSpotAsync(AppDb db, Guid spotId, CancellationToken ct)
    {
        if (await HasOpenSessionAsync(db, spotId, ct)) return;
        await db.Spots.UpdateOneAsync(s => s.Id == spotId && s.State == SpotState.Booked,
            Builders<Spot>.Update.Set(s => s.State, SpotState.Free), cancellationToken: ct);
    }

    internal static Task<bool> HasOpenSessionAsync(AppDb db, Guid spotId, CancellationToken ct) =>
        db.ParkingSessions.Find(s => s.SpotId == spotId && s.Status == SessionStatus.InProgress).AnyAsync(ct);

    static PaymentMethod? ParseMethod(string value) => value?.ToLowerInvariant() switch
    {
        "card" => PaymentMethod.Card,
        "mobile_money" => PaymentMethod.MobileMoney,
        "pay_at_gate" => PaymentMethod.PayAtGate,
        _ => null,
    };

    // ---------- pay ----------

    /// <summary>Mocked instant gateway: card/mobile_money succeed on the spot, pay_at_gate is booked for check-in.</summary>
    public static async Task<Outcome> PayAsync(Guid id, PayInput input, Guid userId, AppDb db, CancellationToken ct)
    {
        var (reservation, notFound, forbidden) = await OwnedOrForbiddenAsync(db, id, userId, ct);
        if (notFound) return Outcome.Missing();
        if (forbidden) return Outcome.Denied();
        var method = ParseMethod(input.Method!);
        if (method is null || method == PaymentMethod.PayAtGate)
            return Outcome.Fail("method", "Choose to pay by card or mobile money here; pay at the gate on arrival.");
        if (reservation!.Status != ReservationStatus.Confirmed)
            return Outcome.Fail("method", "This booking is no longer active.");

        var payment = await db.Payments.Find(p => p.ReservationId == id && p.Status == PaymentStatus.Pending).FirstOrDefaultAsync(ct);
        if (payment is null)
            return Outcome.FlashBack(new Toast("success", "This booking is already paid."));
        await db.Payments.UpdateOneAsync(
            p => p.Id == payment.Id && p.Status == PaymentStatus.Pending,
            Builders<Payment>.Update
                .Set(p => p.Method, method.Value)
                .Set(p => p.Status, PaymentStatus.Paid)
                .Set(p => p.TxRef, $"demo-{payment.Id:N}")
                .Set(p => p.PaidAt, DateTime.UtcNow),
            cancellationToken: ct);
        return Outcome.FlashBack(new Toast("success", "Payment received."));
    }

    // ---------- cancel ----------

    /// <summary>Customer cancel: future occurrences are cancelled, the spot is released, and a refund request is
    /// auto-created when a payment was made and the cancellation rules award money back.</summary>
    public static async Task<Outcome> CancelAsync(Guid id, Guid userId, AppDb db, JobQueue jobs,
        RealtimeEvents realtime, CancellationToken ct)
    {
        var (reservation, notFound, forbidden) = await OwnedOrForbiddenAsync(db, id, userId, ct);
        if (notFound) return Outcome.Missing();
        if (forbidden) return Outcome.Denied();
        if (reservation!.Status != ReservationStatus.Confirmed)
            return Outcome.FlashBack(new Toast("error", "This booking is not active."));

        var spotId = reservation.SpotId;
        if (await HasOpenSessionAsync(db, spotId, ct))
            return Outcome.FlashBack(new Toast("error", "A session is open on this spot. Please check out first."));

        var now = DateTime.UtcNow;
        var nextStart = await db.Occurrences
            .Find(o => o.ReservationId == id && o.Status == OccurrenceStatus.Scheduled && o.StartAtUtc > now)
            .SortBy(o => o.StartAtUtc).Project(o => o.StartAtUtc).FirstOrDefaultAsync(ct);

        var cancelled = await db.Reservations.UpdateOneAsync(
            r => r.Id == id && r.Status == ReservationStatus.Confirmed,
            Builders<Reservation>.Update
                .Set(r => r.Status, ReservationStatus.Cancelled)
                .Set(r => r.CancelledAt, now),
            cancellationToken: ct);
        if (cancelled.ModifiedCount == 0)
            return Outcome.FlashBack(new Toast("info", "This booking was already cancelled."));

        await db.Occurrences.UpdateManyAsync(
            o => o.ReservationId == id && o.Status == OccurrenceStatus.Scheduled && o.StartAtUtc > now,
            Builders<Occurrence>.Update.Set(o => o.Status, OccurrenceStatus.Cancelled), cancellationToken: ct);

        // Exclusivity means at most one confirmation ever holds the spot: Booked → Free when this was the holder.
        await FreeSpotIfBookedAsync(db, spotId, ct);
        await realtime.SpotChangedAsync(reservation.LotId, spotId, "free", ct);

        var payment = await db.Payments.Find(p => p.ReservationId == id && p.Status == PaymentStatus.Paid).FirstOrDefaultAsync(ct);
        if (payment is not null)
        {
            var (rule, amount) = RefundRules.ComputeCents(nextStart, reservation.TotalCents, now);
            if (amount > 0)
            {
                await db.Refunds.InsertOneAsync(new Refund
                {
                    ReservationId = id,
                    PaymentId = payment.Id,
                    RequestedByUserId = userId,
                    Rule = rule,
                    AmountCents = amount,
                    Reason = "Customer cancelled",
                }, cancellationToken: ct);
                await AlertsService.StaffPushAsync(realtime, $"New refund request on {reservation.LotId}", "/admin/refunds", ct);
                await realtime.BoardChangedAsync("refund", ct);
            }
        }

        await AlertsService.CreateAsync(db, realtime, reservation.UserId, "refund",
            "Booking cancelled", $"Reservation {(id.ToString()[..8]).ToUpperInvariant()} was cancelled.", "/account/payments", ct: ct);
        return Outcome.FlashBack(new Toast("success", "Booking cancelled."));
    }

    /// <summary>Booked → Free only when the spot is still counted as booked and nothing is parked on it.</summary>
    public static async Task FreeSpotIfBookedAsync(AppDb db, Guid spotId, CancellationToken ct)
    {
        if (await HasOpenSessionAsync(db, spotId, ct)) return;
        await db.Spots.UpdateOneAsync(s => s.Id == spotId && s.State == SpotState.Booked,
            Builders<Spot>.Update.Set(s => s.State, SpotState.Free).Set(s => s.LastCheckpointAt, DateTime.UtcNow),
            cancellationToken: ct);
    }

    /// <summary>Owner or signed-in admin guard: 404 when missing, 403 when someone else's booking.</summary>
    static async Task<(Reservation? Reservation, bool NotFound, bool Forbidden)> OwnedOrForbiddenAsync(
        AppDb db, Guid id, Guid userId, CancellationToken ct)
    {
        var reservation = await db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (reservation is null) return (null, true, false);
        var role = await db.Users.Find(u => u.Id == userId).Project(u => u.Role).FirstOrDefaultAsync(ct);
        if (reservation.UserId != userId && role != UserRole.Admin)
            return (null, false, true);
        return (reservation, false, false);
    }
}

/// <summary>Domain validation failures carry a user-facing message the endpoints map onto the "window" input.</summary>
public sealed class BookingException(string message) : Exception(message);
