using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
using CarParker.Features.Insurance;
using CarParker.Features.Lots;
using CarParker.Infrastructure;

namespace CarParker.Features.Admin;

/// <summary>Admin console reads and actions: overview, lots and spot overrides, bookings, sessions, refunds and users.</summary>
public sealed class AdminService(AppDb db, LotsService lots, BookingsService bookings, JobQueue jobs, RealtimeEvents realtime)
{
    /// <summary>KPI counts for GET /admin. The queries are independent and the Mongo driver is thread-safe, so they run together.</summary>
    public async Task<AdminOverview> OverviewAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var overdueBefore = now.AddMinutes(-GuardService.OverstayGraceMinutes);

        var refundsPending = db.Refunds.CountDocumentsAsync(r => r.Status == RefundStatus.Requested, cancellationToken: ct);
        var claimsPending = db.PolicyClaims.CountDocumentsAsync(c => c.Status == ClaimStatus.Submitted || c.Status == ClaimStatus.UnderReview, cancellationToken: ct);
        var openIncidents = db.SecurityIncidents.CountDocumentsAsync(i => i.Status == IncidentStatus.Open, cancellationToken: ct);
        var overdue = CountOverdueSessionsAsync(overdueBefore, ct);
        var noShows = db.Occurrences.CountDocumentsAsync(o => o.Status == OccurrenceStatus.NoShow && o.StartAtUtc >= today.AddDays(-1), cancellationToken: ct);
        var reservationsToday = db.Reservations.CountDocumentsAsync(r => r.CreatedAt >= today, cancellationToken: ct);
        var sessionsToday = db.ParkingSessions.CountDocumentsAsync(s => s.EnteredAtUtc >= today, cancellationToken: ct);
        var revenue = db.Payments.Find(p => p.Status == PaymentStatus.Paid && p.PaidAt >= today).Project(p => p.AmountCents).ToListAsync(ct);
        var cards = ActiveCardsAsync(ct);
        await Task.WhenAll(refundsPending, claimsPending, openIncidents, overdue, noShows, reservationsToday, sessionsToday, revenue, cards);

        return new AdminOverview(refundsPending.Result, claimsPending.Result, overdue.Result, openIncidents.Result, noShows.Result,
            reservationsToday.Result, sessionsToday.Result, revenue.Result.Sum(), cards.Result);
    }

    async Task<LotCard[]> ActiveCardsAsync(CancellationToken ct) => await lots.CardsAsync(await lots.ActiveAsync(ct), ct);

    async Task<long> CountOverdueSessionsAsync(DateTime overdueBefore, CancellationToken ct)
    {
        var occurrenceIds = await db.ParkingSessions.Find(s => s.Status == SessionStatus.InProgress).Project(s => s.OccurrenceId).ToListAsync(ct);
        return occurrenceIds.Count == 0 ? 0 : await db.Occurrences.CountDocumentsAsync(o => occurrenceIds.Contains(o.Id) && o.EndAtUtc < overdueBefore, cancellationToken: ct);
    }

    /// <summary>All lots, active first, as cards for the admin table.</summary>
    public async Task<LotCard[]> LotsAsync(CancellationToken ct) =>
        await lots.CardsAsync(await db.Lots.Find(_ => true).SortByDescending(l => l.IsActive).ThenBy(l => l.Name).ToListAsync(ct), ct);

    /// <summary>Creates a lot with its spot grid in one transaction.</summary>
    public async Task<Lot> CreateLotAsync(LotInput input, CancellationToken ct)
    {
        if (LotHours.Error(input.Open24Hours, input.Opens, input.Closes, input.TimeZoneId) is { } error) throw new BookingException(error);
        var lot = new Lot
        {
            Name = input.Name!.Trim(),
            Address = input.Address!.Trim(),
            Area = input.Area!.Trim(),
            LevelCount = Math.Clamp(input.LevelCount, 1, 4),
            RatePerHourCents = input.RatePerHourCents,
            DailyMaxCents = input.DailyMaxCents,
            ContactPhone = input.ContactPhone,
            IsActive = input.IsActive,
            CoverageCents = input.CoverageCents,
            Open24Hours = input.Open24Hours, Opens = input.Opens, Closes = input.Closes, TimeZoneId = input.TimeZoneId,
        };
        await db.TransactionAsync(async session =>
        {
            await db.Lots.InsertOneAsync(session, lot, cancellationToken: ct);
            await db.Spots.InsertManyAsync(session, SpotGrid.For(lot), cancellationToken: ct);
        }, ct);
        await realtime.LotChangedAsync(lot.Id, ct);
        return lot;
    }

    public async Task<AdminLotDetail?> LotAsync(Guid id, CancellationToken ct)
    {
        var lot = await db.Lots.Find(l => l.Id == id).FirstOrDefaultAsync(ct);
        if (lot is null) return null;
        var card = lots.CardsAsync([lot], ct);
        var spots = db.Spots.Find(s => s.LotId == id).SortBy(s => s.Level).ThenBy(s => s.Label).ToListAsync(ct);
        var upcoming = db.Reservations.Find(r => r.LotId == id && r.Status == ReservationStatus.Confirmed).SortBy(r => r.CreatedAt).Limit(10).ToListAsync(ct);
        await Task.WhenAll(card, spots, upcoming);
        return new AdminLotDetail(card.Result[0],
            spots.Result.Select(s => new AdminLotSpot(s.Id, s.Label, s.Level, s.LastCheckpointAt, s.CheckpointNote,
                s.Type.ToString().ToLowerInvariant(), s.State.ToString().ToLowerInvariant())).ToArray(),
            await bookings.ViewsAsync(upcoming.Result, ct));
    }

    public async Task<Outcome> UpdateLotAsync(Guid id, LotInput input, CancellationToken ct)
    {
        if (LotHours.Error(input.Open24Hours, input.Opens, input.Closes, input.TimeZoneId) is { } error) return Outcome.Fail("opens", error);
        var result = await db.Lots.UpdateOneAsync(l => l.Id == id,
            Builders<Lot>.Update
                .Set(l => l.Name, input.Name!.Trim())
                .Set(l => l.Address, input.Address!.Trim())
                .Set(l => l.Area, input.Area!.Trim())
                .Set(l => l.LevelCount, Math.Clamp(input.LevelCount, 1, 4))
                .Set(l => l.RatePerHourCents, input.RatePerHourCents)
                .Set(l => l.DailyMaxCents, input.DailyMaxCents)
                .Set(l => l.ContactPhone, input.ContactPhone)
                .Set(l => l.IsActive, input.IsActive)
                .Set(l => l.Open24Hours, input.Open24Hours).Set(l => l.Opens, input.Opens).Set(l => l.Closes, input.Closes).Set(l => l.TimeZoneId, input.TimeZoneId)
                .Set(l => l.CoverageCents, input.CoverageCents)
                .Set(l => l.UpdatedAt, DateTime.UtcNow),
            cancellationToken: ct);
        if (result.MatchedCount == 0) return Outcome.FlashBack(new Toast("error", "That lot no longer exists."));
        await realtime.LotChangedAsync(id, ct);
        return Outcome.FlashBack(new Toast("success", "Lot updated."));
    }

    /// <summary>Block a spot (not while a car is on it) or clear a blocked one.</summary>
    public async Task<Outcome> OverrideSpotAsync(Guid lotId, Guid spotId, SpotOverrideInput input, CancellationToken ct)
    {
        var spot = await db.Spots.Find(s => s.Id == spotId && s.LotId == lotId && s.Active).FirstOrDefaultAsync(ct);
        if (spot is null) return Outcome.FlashBack(new Toast("error", "Spot not found."));
        var block = (input.Action ?? "").ToLowerInvariant() switch { "block" => true, "clear" => false, _ => (bool?)null };
        if (block is null) return Outcome.Fail("action", "Use block or clear.");
        if (block == true && spot.State == SpotState.Occupied)
            return Outcome.FlashBack(new Toast("error", "A session is open on that spot. Check the car out first."));

        var (to, note) = block == true ? (SpotState.Blocked, input.Note ?? "Blocked by admin") : (SpotState.Free, input.Note ?? "Cleared by admin");
        var filter = block == true
            ? Builders<Spot>.Filter.Where(s => s.Id == spotId && s.State != SpotState.Occupied)
            : Builders<Spot>.Filter.Where(s => s.Id == spotId && s.State == SpotState.Blocked);
        await db.Spots.UpdateOneAsync(filter,
            Builders<Spot>.Update.Set(s => s.State, to).Set(s => s.LastCheckpointAt, DateTime.UtcNow).Set(s => s.CheckpointNote, note),
            cancellationToken: ct);
        await realtime.SpotChangedAsync(lotId, spotId, to.ToString().ToLowerInvariant(), ct);
        return Outcome.FlashBack(new Toast("success", $"{spot.Label} {(block == true ? "blocked" : "cleared")}."));
    }

    // ---------- bookings & sessions ----------

    public async Task<AdminBookingsPage> BookingsAsync(string? status, CancellationToken ct, string? q = null, int page = 1)
    {
        const int pageSize = 25;
        page = Math.Max(page, 1);
        var filter = Builders<Reservation>.Filter.Empty;
        if (Enum.TryParse<ReservationStatus>(status, ignoreCase: true, out var wanted))
            filter &= Builders<Reservation>.Filter.Eq(r => r.Status, wanted);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = new MongoDB.Bson.BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(q.Trim()), "i");
            var matchedUsers = await db.Users.Find(Builders<User>.Filter.Regex(u => u.Name, pattern) | Builders<User>.Filter.Regex(u => u.Email, pattern)).Project(u => u.Id).ToListAsync(ct);
            var lotIds = await db.Lots.Find(Builders<Lot>.Filter.Regex(l => l.Name, pattern)).Project(l => l.Id).ToListAsync(ct);
            filter &= Builders<Reservation>.Filter.In(r => r.UserId, matchedUsers) | Builders<Reservation>.Filter.In(r => r.LotId, lotIds) | Builders<Reservation>.Filter.Regex(r => r.PlateNo, pattern);
        }
        var rows = await db.Reservations.Find(filter).SortByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((int)Math.Min(((long)page - 1) * pageSize, int.MaxValue)).Limit(pageSize + 1).ToListAsync(ct);
        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(pageSize);
        var userIds = rows.Select(r => r.UserId).Distinct().ToArray();
        var users = (await db.Users.Find(u => userIds.Contains(u.Id)).Project(u => new { u.Id, u.Name, u.Email }).ToListAsync(ct)).ToDictionary(u => u.Id);
        var views = await bookings.ViewsAsync(rows, ct);
        return new AdminBookingsPage(views.Zip(rows, (view, r) => new AdminBookingRow(view, users.GetValueOrDefault(r.UserId)?.Name, users.GetValueOrDefault(r.UserId)?.Email)).ToArray(), page, hasMore);
    }

    public async Task<AdminBookingDetail?> BookingAsync(Guid id, CancellationToken ct)
    {
        var reservation = await db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (reservation is null) return null;
        var view = (await bookings.ViewsAsync([reservation], ct))[0];
        var user = await db.Users.Find(u => u.Id == reservation.UserId).Project(u => new AdminUserRef(u.Id, u.Name, u.Email)).FirstOrDefaultAsync(ct);
        return new AdminBookingDetail(view, user);
    }

    public async Task<AdminSessionsPage> SessionsAsync(CancellationToken ct, string? q = null, int openPage = 1, int exitedPage = 1)
    {
        const int pageSize = 25;
        openPage = Math.Max(openPage, 1);
        exitedPage = Math.Max(exitedPage, 1);
        var filter = Builders<ParkingSession>.Filter.Empty;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = new MongoDB.Bson.BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(q.Trim()), "i");
            var matchedLots = await db.Lots.Find(Builders<Lot>.Filter.Regex(l => l.Name, pattern)).Project(l => l.Id).ToListAsync(ct);
            var matchedSpots = await db.Spots.Find(Builders<Spot>.Filter.Regex(s => s.Label, pattern)).Project(s => s.Id).ToListAsync(ct);
            filter &= Builders<ParkingSession>.Filter.Regex(s => s.PlateNo, pattern) | Builders<ParkingSession>.Filter.In(s => s.LotId, matchedLots) | Builders<ParkingSession>.Filter.In(s => s.SpotId, matchedSpots);
        }
        var openFilter = filter & Builders<ParkingSession>.Filter.Eq(s => s.Status, SessionStatus.InProgress);
        var exitedFilter = filter & Builders<ParkingSession>.Filter.Eq(s => s.Status, SessionStatus.Exited);
        var openCountTask = db.ParkingSessions.CountDocumentsAsync(openFilter, cancellationToken: ct);
        var exitedCountTask = db.ParkingSessions.CountDocumentsAsync(exitedFilter, cancellationToken: ct);
        var openTask = db.ParkingSessions.Find(openFilter).SortBy(s => s.EnteredAtUtc).ThenBy(s => s.Id)
            .Skip((int)Math.Min(((long)openPage - 1) * pageSize, int.MaxValue)).Limit(pageSize + 1).ToListAsync(ct);
        var exitedTask = db.ParkingSessions.Find(exitedFilter).SortByDescending(s => s.ExitedAtUtc).ThenByDescending(s => s.Id)
            .Skip((int)Math.Min(((long)exitedPage - 1) * pageSize, int.MaxValue)).Limit(pageSize + 1).ToListAsync(ct);
        await Task.WhenAll(openCountTask, exitedCountTask, openTask, exitedTask);
        var open = openTask.Result;
        var exited = exitedTask.Result;
        var openHasMore = open.Count > pageSize;
        var exitedHasMore = exited.Count > pageSize;
        if (openHasMore) open.RemoveAt(pageSize);
        if (exitedHasMore) exited.RemoveAt(pageSize);
        var all = open.Concat(exited).ToList();
        var lotIds = all.Select(s => s.LotId).Distinct().ToArray();
        var spotIds = all.Select(s => s.SpotId).Distinct().ToArray();
        var lotNames = (await db.Lots.Find(l => lotIds.Contains(l.Id)).Project(l => new { l.Id, l.Name }).ToListAsync(ct)).ToDictionary(l => l.Id, l => l.Name);
        var labels = (await db.Spots.Find(s => spotIds.Contains(s.Id)).Project(s => new { s.Id, s.Label }).ToListAsync(ct)).ToDictionary(s => s.Id, s => s.Label);
        AdminSessionRow Row(ParkingSession s) => new(s.Id, s.PlateNo, s.EnteredAtUtc, s.ExitedAtUtc, s.CancelReason,
            s.Status.ToString().ToLowerInvariant(), lotNames.GetValueOrDefault(s.LotId), labels.GetValueOrDefault(s.SpotId));
        return new AdminSessionsPage(open.Select(Row).ToArray(), openPage, openHasMore, openCountTask.Result,
            exited.Select(Row).ToArray(), exitedPage, exitedHasMore, exitedCountTask.Result);
    }

    // ---------- refunds ----------

    public async Task<AdminRefundsPage> RefundsAsync(int pendingPage, int decidedPage, CancellationToken ct)
    {
        const int pageSize = 25;
        pendingPage = Math.Max(pendingPage, 1);
        decidedPage = Math.Max(decidedPage, 1);
        var pending = await db.Refunds.Find(r => r.Status == RefundStatus.Requested).SortBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip((int)Math.Min(((long)pendingPage - 1) * pageSize, int.MaxValue)).Limit(pageSize + 1).ToListAsync(ct);
        var decided = await db.Refunds.Find(r => r.Status != RefundStatus.Requested).SortByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((int)Math.Min(((long)decidedPage - 1) * pageSize, int.MaxValue)).Limit(pageSize + 1).ToListAsync(ct);
        var pendingHasMore = pending.Count > pageSize;
        var decidedHasMore = decided.Count > pageSize;
        if (pendingHasMore) pending.RemoveAt(pageSize);
        if (decidedHasMore) decided.RemoveAt(pageSize);
        var pendingRows = await RefundRowsAsync(pending, ct);
        var decidedRows = await RefundRowsAsync(decided, ct);
        return new AdminRefundsPage(pendingRows, pendingPage, pendingHasMore, decidedRows, decidedPage, decidedHasMore);
    }

    async Task<AdminRefundRow[]> RefundRowsAsync(IReadOnlyCollection<Refund> refunds, CancellationToken ct)
    {
        var reservationIds = refunds.Select(r => r.ReservationId).Distinct().ToArray();
        var reservations = (await db.Reservations.Find(r => reservationIds.Contains(r.Id)).Project(r => new { r.Id, r.LotId, r.SpotId, r.TotalCents }).ToListAsync(ct))
            .ToDictionary(r => r.Id);
        var lotIds = reservations.Values.Select(r => r.LotId).Distinct().ToArray();
        var spotIds = reservations.Values.Select(r => r.SpotId).Distinct().ToArray();
        var userIds = refunds.Select(r => r.RequestedByUserId).Distinct().ToArray();
        var lotNames = (await db.Lots.Find(l => lotIds.Contains(l.Id)).Project(l => new { l.Id, l.Name }).ToListAsync(ct)).ToDictionary(l => l.Id, l => l.Name);
        var labels = (await db.Spots.Find(s => spotIds.Contains(s.Id)).Project(s => new { s.Id, s.Label }).ToListAsync(ct)).ToDictionary(s => s.Id, s => s.Label);
        var userNames = (await db.Users.Find(u => userIds.Contains(u.Id)).Project(u => new { u.Id, u.Name }).ToListAsync(ct)).ToDictionary(u => u.Id, u => u.Name);
        return refunds.Select(r =>
        {
            var reservation = reservations.GetValueOrDefault(r.ReservationId);
            return new AdminRefundRow(r.Id, r.ReservationId, r.PaymentId, r.Rule, r.AmountCents, r.Reason, r.CreatedAt,
                r.DecisionNote, r.ApprovedByUserId, r.PaidAt, r.Status.ToString().ToLowerInvariant(),
                reservation is null ? null : lotNames.GetValueOrDefault(reservation.LotId),
                reservation is null ? null : labels.GetValueOrDefault(reservation.SpotId),
                userNames.GetValueOrDefault(r.RequestedByUserId), reservation?.TotalCents ?? 0);
        }).ToArray();
    }

    /// <summary>Approves a requested refund at or below the paid amount.</summary>
    public async Task<Outcome> ApproveRefundAsync(Guid id, RefundDecisionInput input, Guid adminId, CancellationToken ct)
    {
        var refund = await db.Refunds.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (refund is null) return Outcome.Missing();
        var cap = await db.Payments.Find(p => p.Id == refund.PaymentId).Project(p => (long?)p.AmountCents).FirstOrDefaultAsync(ct) ?? refund.AmountCents;
        if (input.AmountCents > cap) return Outcome.Fail("amountCents", $"Refunds cannot exceed the paid amount (${cap / 100.0:F2}).");
        var approved = await db.Refunds.UpdateOneAsync(r => r.Id == id && r.Status == RefundStatus.Requested,
            Builders<Refund>.Update.Set(r => r.AmountCents, input.AmountCents).Set(r => r.Status, RefundStatus.Approved)
                .Set(r => r.DecisionNote, input.Note).Set(r => r.ApprovedByUserId, adminId),
            cancellationToken: ct);
        if (approved.ModifiedCount == 0) return Outcome.FlashBack(new Toast("info", "That refund was already decided."));
        await realtime.BoardChangedAsync("refund", ct);
        return Outcome.FlashBack(new Toast("success", "Refund approved. Pay it to complete the flow."));
    }

    /// <summary>Pays an approved refund, marks the source payment refunded and queues the email, all in one transaction.</summary>
    public async Task<Outcome> PayRefundAsync(Guid id, CancellationToken ct)
    {
        var paid = await db.TransactionAsync(async session =>
        {
            var refund = await db.Refunds.FindOneAndUpdateAsync(session, r => r.Id == id && r.Status == RefundStatus.Approved,
                Builders<Refund>.Update.Set(r => r.Status, RefundStatus.Paid).Set(r => r.PaidAt, DateTime.UtcNow), cancellationToken: ct);
            if (refund is null) return false;
            await db.Payments.UpdateOneAsync(session, p => p.Id == refund.PaymentId,
                Builders<Payment>.Update.Set(p => p.Status, PaymentStatus.Refunded), cancellationToken: ct);
            await jobs.EnqueueAsync(db, new RefundPaidEmail(id), session, ct: ct);
            return true;
        }, ct);
        if (!paid)
            return await db.Refunds.Find(r => r.Id == id).AnyAsync(ct)
                ? Outcome.FlashBack(new Toast("info", "Only approved refunds can be paid."))
                : Outcome.Missing();
        jobs.Wake();
        await realtime.BoardChangedAsync("refund", ct);
        return Outcome.FlashBack(new Toast("success", "Refund marked paid."));
    }

    public async Task<Outcome> RejectRefundAsync(Guid id, RefundDecisionInput input, CancellationToken ct)
    {
        var rejected = await db.Refunds.UpdateOneAsync(r => r.Id == id && r.Status == RefundStatus.Requested,
            Builders<Refund>.Update.Set(r => r.Status, RefundStatus.Rejected).Set(r => r.DecisionNote, input.Note ?? "Rejected"),
            cancellationToken: ct);
        if (rejected.ModifiedCount == 0) return Outcome.FlashBack(new Toast("info", "That refund was already decided."));
        await realtime.BoardChangedAsync("refund", ct);
        return Outcome.FlashBack(new Toast("success", "Refund rejected."));
    }

    // ---------- users ----------

    public async Task<AdminUsersPage> UsersAsync(string? q, string? role, int page, CancellationToken ct)
    {
        const int pageSize = 25;
        page = Math.Max(page, 1);
        var filter = Builders<User>.Filter.Empty;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = new MongoDB.Bson.BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(q.Trim()), "i");
            filter &= Builders<User>.Filter.Regex(u => u.Name, pattern) | Builders<User>.Filter.Regex(u => u.Email, pattern);
        }
        if (Enum.TryParse<UserRole>(role, true, out var wanted) && Enum.IsDefined(wanted))
            filter &= Builders<User>.Filter.Eq(u => u.Role, wanted);
        var users = await db.Users.Find(filter).SortByDescending(u => u.CreatedAt).ThenByDescending(u => u.Id)
            .Skip((int)Math.Min(((long)page - 1) * pageSize, int.MaxValue)).Limit(pageSize + 1).ToListAsync(ct);
        var hasMore = users.Count > pageSize;
        if (hasMore) users.RemoveAt(pageSize);
        return new AdminUsersPage(users.Select(u => new AdminUserRow(u.Id, u.Name, u.Email, u.EmailVerifiedAt, u.CreatedAt, u.Role.ToString().ToLowerInvariant())).ToArray(), page, hasMore);
    }

    public async Task<AdminUserDetail?> UserAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.Find(u => u.Id == id).FirstOrDefaultAsync(ct);
        if (user is null) return null;
        var rows = await db.Reservations.Find(r => r.UserId == id).SortByDescending(r => r.CreatedAt).Limit(100).ToListAsync(ct);
        return new AdminUserDetail(new AdminUserRow(user.Id, user.Name, user.Email, user.EmailVerifiedAt, user.CreatedAt, user.Role.ToString().ToLowerInvariant()), await bookings.ViewsAsync(rows, ct));
    }

    public async Task<Outcome> ToggleVerifyAsync(Guid id, CancellationToken ct)
    {
        var verifiedAt = await db.Users.Find(u => u.Id == id).Project(u => new { u.EmailVerifiedAt }).FirstOrDefaultAsync(ct);
        if (verifiedAt is null) return Outcome.Missing();
        var wasVerified = verifiedAt.EmailVerifiedAt is not null;
        await db.Users.UpdateOneAsync(u => u.Id == id,
            Builders<User>.Update.Set(u => u.EmailVerifiedAt, wasVerified ? null : DateTime.UtcNow), cancellationToken: ct);
        return Outcome.FlashBack(new Toast("success", wasVerified ? "Verification revoked." : "Email marked verified."));
    }
}
