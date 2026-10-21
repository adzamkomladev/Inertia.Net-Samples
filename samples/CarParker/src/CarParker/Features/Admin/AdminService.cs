using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
using CarParker.Features.Incidents;
using CarParker.Features.Insurance;
using CarParker.Features.Lots;

namespace CarParker.Features.Admin;

/// <summary>Admin console reads and actions: overview KPIs, lots &amp; spot overrides, bookings, sessions,
/// refunds, insurance and users. Returns plain data + Outcome; the Admin.cs route handlers compose Inertia.</summary>
public static partial class AdminService
{
    // ---------- overview & lots ----------

    /// <summary>KPI counts + needs-attention strip for GET /admin.</summary>
    public static async Task<AdminOverview> OverviewAsync(AppDb db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var refundsPending = await db.Refunds.CountDocumentsAsync(r => r.Status == RefundStatus.Requested, cancellationToken: ct);
        var claimsPending = await db.PolicyClaims.CountDocumentsAsync(c => c.Status == ClaimStatus.Submitted || c.Status == ClaimStatus.UnderReview, cancellationToken: ct);
        var openIncidnts = await db.SecurityIncidents.CountDocumentsAsync(i => i.Status == IncidentStatus.Open, cancellationToken: ct);
        var openSessions = await db.ParkingSessions.Find(s => s.Status == SessionStatus.InProgress).ToListAsync(ct);
        var occurrenceIds = openSessions.Select(s => s.OccurrenceId).ToArray();
        var occurrences = occurrenceIds.Length == 0 ? [] : await db.Occurrences.Find(o => occurrenceIds.Contains(o.Id)).ToListAsync(ct);
        var byId = occurrences.ToDictionary(o => o.Id);
        var overdue = openSessions.Where(s => byId.TryGetValue(s.OccurrenceId, out var occ) && occ.EndAtUtc + TimeSpan.FromMinutes(GuardService.OverstayGraceMinutes) < now).ToList();
        var noShowsToday = await db.Occurrences.CountDocumentsAsync(o => o.Status == OccurrenceStatus.NoShow && o.StartAtUtc >= today.AddDays(-1), cancellationToken: ct);
        var reservationsToday = await db.Reservations.CountDocumentsAsync(r => r.CreatedAt >= today, cancellationToken: ct);
        var sessionsToday = await db.ParkingSessions.CountDocumentsAsync(s => s.EnteredAtUtc >= today, cancellationToken: ct);
        var revenueToday = await db.Payments.Find(p => p.Status == PaymentStatus.Paid && p.PaidAt >= today).ToListAsync(ct);
        var lots = await LotsService.ActiveAsync(db, ct);
        return new AdminOverview(
            refundsPending, claimsPending, overdue.Count, openIncidnts, noShowsToday,
            reservationsToday, sessionsToday, revenueToday.Sum(p => p.AmountCents),
            await LotsService.CardsAsync(db, lots, ct));
    }

    /// <summary>All lots (active + inactive) as cards for the admin lots table.</summary>
    public static async Task<LotCard[]> LotsAsync(AppDb db, CancellationToken ct)
    {
        var lots = await LotsService.ActiveAsync(db, ct);
        var inactive = await db.Lots.Find(l => !l.IsActive).SortBy(l => l.Name).ToListAsync(ct);
        return await LotsService.CardsAsync(db, [.. lots, .. inactive], ct);
    }

    /// <summary>Creates a lot, seeds its A–D spot grid, and queues it for search indexing.</summary>
    public static async Task<Lot> CreateLotAsync(LotInput input, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
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
        };
        await db.Lots.InsertOneAsync(lot, cancellationToken: ct);
        // New lot becomes searchable: bump the index version and let the queue pull it in.
        await SearchIndexing.RequestAsync(db, jobs, lot.Id, 1, ct);
        await SeedSpotsAsync(db, lot.Id, lot.LevelCount, ct);
        await realtime.LotChangedAsync(lot.Id, ct);
        return lot;
    }

    /// <summary>Create a standard grid: level sections (A..) × 12 spots per section.</summary>
    internal static async Task SeedSpotsAsync(AppDb db, Guid lotId, int levelCount, CancellationToken ct)
    {
        var spots = new List<Spot>();
        foreach (var (level, section) in SpotGrid.Plane(Math.Clamp(levelCount, 1, 4)))
        {
            var handicapChance = 0; var evChance = 0; var coveredChance = 0; var compactChance = 0;
            foreach (var number in Enumerable.Range(1, 12))
            {
                var type = SpotType.Standard;
                if (number == 1) type = SpotType.Handicap;
                else if (number == 2) type = SpotType.Ev;
                else if (number <= 4) type = SpotType.Compact;
                else if (number >= 11) type = SpotType.Covered;
                _ = handicapChance + evChance + coveredChance + compactChance; // keep ternaries readable
                spots.Add(new Spot { LotId = lotId, Label = $"{section}-{number:00}", Type = type, Level = level });
            }
        }
        await db.Spots.InsertManyAsync(spots, cancellationToken: ct);
    }

    /// <summary>Lot detail: card row, full spot grid, and the next 10 confirmed reservations.</summary>
    internal static async Task<AdminLotDetail?> LotAsync(Guid id, AppDb db, CancellationToken ct)
    {
        var lot = await db.Lots.Find(l => l.Id == id).FirstOrDefaultAsync(ct);
        if (lot is null)
            return null;
        var spots = await db.Spots.Find(s => s.LotId == id).SortBy(s => s.Level).ThenBy(s => s.Label).ToListAsync(ct);
        var upcoming = await db.Reservations.Find(r => r.LotId == id && r.Status == ReservationStatus.Confirmed)
            .SortBy(r => r.CreatedAt).Limit(50).ToListAsync(ct);
        return new AdminLotDetail(
            (await LotsService.CardsAsync(db, [lot], ct))[0],
            spots.Select(s => new AdminLotSpot(
                s.Id, s.Label, s.Level, s.LastCheckpointAt, s.CheckpointNote,
                s.Type.ToString().ToLowerInvariant(), s.State.ToString().ToLowerInvariant())).ToArray(),
            (await BookingsService.BookingViewsAsync(db, upcoming, ct)).Take(10).ToArray());
    }

    /// <summary>Applies admin edits to a lot; bumps the index version and re-queues search.</summary>
    public static async Task<Outcome> UpdateLotAsync(Guid id, LotInput input, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var lot = await db.Lots.Find(l => l.Id == id).FirstOrDefaultAsync(ct);
        if (lot is null)
            return Outcome.FlashBack(new Toast("error", "That lot no longer exists."));
        var result = await db.Lots.UpdateOneAsync(
            l => l.Id == id,
            Builders<Lot>.Update
                .Set(l => l.Name, input.Name!.Trim())
                .Set(l => l.Address, input.Address!.Trim())
                .Set(l => l.Area, input.Area!.Trim())
                .Set(l => l.LevelCount, Math.Clamp(input.LevelCount, 1, 4))
                .Set(l => l.RatePerHourCents, input.RatePerHourCents)
                .Set(l => l.DailyMaxCents, input.DailyMaxCents)
                .Set(l => l.ContactPhone, input.ContactPhone)
                .Set(l => l.IsActive, input.IsActive)
                .Set(l => l.CoverageCents, input.CoverageCents)
                .Set(l => l.UpdatedAt, DateTime.UtcNow)
                .Inc(l => l.IndexVersion, 1),
            cancellationToken: ct);
        if (result.ModifiedCount == 0)
            return Outcome.FlashBack(new Toast("error", "That lot no longer exists."));
        await SearchIndexing.RequestAsync(db, jobs, id, Math.Max(1, lot.IndexVersion) + 1, ct);
        await realtime.LotChangedAsync(id, ct);
        return Outcome.FlashBack(new Toast("success", "Lot updated."));
    }

    /// <summary>Manual override: block (state=blocked with note) or clear (only frees a blocked spot).</summary>
    public static async Task<Outcome> OverrideSpotAsync(Guid lotId, Guid spotId, SpotOverrideInput input, AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        var spot = await db.Spots.Find(s => s.Id == spotId && s.LotId == lotId && s.Active).FirstOrDefaultAsync(ct);
        if (spot is null)
            return Outcome.FlashBack(new Toast("error", "Spot not found."));
        var now = DateTime.UtcNow;
        switch ((input.Action ?? "").ToLowerInvariant())
        {
            case "block":
                if (spot.State == SpotState.Occupied)
                    return Outcome.FlashBack(new Toast("error", "A session is open on that spot. Check the car out first."));
                await db.Spots.UpdateOneAsync(
                    s => s.Id == spotId && s.State != SpotState.Occupied,
                    Builders<Spot>.Update
                        .Set(s => s.State, SpotState.Blocked)
                        .Set(s => s.LastCheckpointAt, now)
                        .Set(s => s.CheckpointNote, input.Note ?? "Blocked by admin"),
                    cancellationToken: ct);
                await realtime.SpotChangedAsync(lotId, spotId, "blocked", ct);
                return Outcome.FlashBack(new Toast("success", $"{spot.Label} blocked."));
            case "clear":
                await db.Spots.UpdateOneAsync(
                    s => s.Id == spotId && s.State == SpotState.Blocked,
                    Builders<Spot>.Update
                        .Set(s => s.State, SpotState.Free)
                        .Set(s => s.LastCheckpointAt, now)
                        .Set(s => s.CheckpointNote, input.Note ?? "Cleared by admin"),
                    cancellationToken: ct);
                await realtime.SpotChangedAsync(lotId, spotId, "free", ct);
                return Outcome.FlashBack(new Toast("success", $"{spot.Label} cleared."));
            default:
                return Outcome.Fail("action", "Use block or clear.");
        }
    }

    // ---------- bookings & sessions ----------

    /// <summary>Booking list (optionally status-filtered, newest first) with customer names.</summary>
    internal static async Task<AdminBookingRow[]> BookingsAsync(string? status, AppDb db, CancellationToken ct)
    {
        var filter = Builders<Reservation>.Filter.Empty;
        if (status is { Length: > 0 } && Enum.TryParse<ReservationStatus>(status, ignoreCase: true, out var wanted))
            filter &= Builders<Reservation>.Filter.Eq(r => r.Status, wanted);
        var rows = await db.Reservations.Find(filter).SortByDescending(r => r.CreatedAt).Limit(100).ToListAsync(ct);
        var userIds = rows.Select(r => r.UserId).Distinct().ToArray();
        var users = userIds.Length == 0 ? [] : await db.Users.Find(u => userIds.Contains(u.Id))
            .Project(u => new { u.Id, u.Name, u.Email }).ToListAsync(ct);
        var usersById = users.ToDictionary(u => u.Id);
        var views = await BookingsService.BookingViewsAsync(db, rows, ct);
        return views.Zip(rows).Select(pair =>
        {
            var (view, reservation) = pair;
            var user = usersById.GetValueOrDefault(reservation.UserId);
            return new AdminBookingRow(view, user?.Name, user?.Email);
        }).ToArray();
    }

    /// <summary>Booking detail with the customer row for the admin drawer.</summary>
    internal static async Task<AdminBookingDetail?> BookingAsync(Guid id, AppDb db, CancellationToken ct)
    {
        var reservation = await db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (reservation is null)
            return null;
        var view = (await BookingsService.BookingViewsAsync(db, [reservation], ct))[0];
        var user = await db.Users.Find(u => u.Id == reservation.UserId).Project(u => new { u.Id, u.Name, u.Email }).FirstOrDefaultAsync(ct);
        return new AdminBookingDetail(view, user is null ? null : new AdminUserRef(user.Id, user.Name, user.Email));
    }

    /// <summary>Open + recent exited sessions with lot/spot labels.</summary>
    public static async Task<AdminSessionsPage> SessionsAsync(AppDb db, CancellationToken ct)
    {
        var open = await db.ParkingSessions.Find(s => s.Status == SessionStatus.InProgress).SortBy(s => s.EnteredAtUtc).ToListAsync(ct);
        var exited = await db.ParkingSessions.Find(s => s.Status == SessionStatus.Exited).SortByDescending(s => s.ExitedAtUtc).Limit(100).ToListAsync(ct);
        var lotIds = open.Concat(exited).Select(s => s.LotId).Distinct().ToArray();
        var lotNames = lotIds.Length == 0 ? [] : await db.Lots.Find(l => lotIds.Contains(l.Id)).Project(l => new { l.Id, l.Name }).ToListAsync(ct);
        var namesById = lotNames.ToDictionary(x => x.Id, x => x.Name);
        var spotIds = open.Concat(exited).Select(s => s.SpotId).Distinct().ToArray();
        var spots = spotIds.Length == 0 ? [] : await db.Spots.Find(s => spotIds.Contains(s.Id)).ToListAsync(ct);
        var labelsById = spots.ToDictionary(s => s.Id, s => s.Label);
        AdminSessionRow Row(ParkingSession s) => new(
            s.Id, s.PlateNo, s.EnteredAtUtc, s.ExitedAtUtc, s.CancelReason,
            s.Status.ToString().ToLowerInvariant(),
            namesById.GetValueOrDefault(s.LotId),
            labelsById.GetValueOrDefault(s.SpotId));
        return new AdminSessionsPage(open.Select(Row).ToArray(), exited.Select(Row).ToArray());
    }

    /// <summary>Force-closes an open session (shared Guard close) and refreshes the board.</summary>
    public static async Task<Outcome> ForceCloseAsync(Guid id, Guid operatorId, string? note, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var session = await db.ParkingSessions.Find(s => s.Id == id).FirstOrDefaultAsync(ct);
        if (session is null)
            return Outcome.Missing();
        if (session.Status != SessionStatus.InProgress)
            return Outcome.FlashBack(new Toast("info", "That session already ended."));
        var closed = await GuardService.CloseSessionAsync(db, jobs, session, operatorId, "force_close" + (string.IsNullOrWhiteSpace(note) ? "" : $": {note}"), realtime, ct);
        if (closed)
        {
            await realtime.SpotChangedAsync(session.LotId, session.SpotId, "free", ct);
            await realtime.BoardChangedAsync("session", ct);
        }
        return Outcome.FlashBack(new Toast("success", "Session force-closed."));
    }

    // ---------- refunds ----------

    /// <summary>Pending + decided refunds with booking labels for the admin table.</summary>
    public static async Task<AdminRefundsPage> RefundsAsync(AppDb db, CancellationToken ct)
    {
        var pending = await db.Refunds.Find(r => r.Status == RefundStatus.Requested).SortBy(r => r.CreatedAt).ToListAsync(ct);
        var decided = await db.Refunds.Find(r => r.Status != RefundStatus.Requested).SortByDescending(r => r.CreatedAt).Limit(100).ToListAsync(ct);
        return new AdminRefundsPage(await RefundRowsAsync(db, pending, ct), await RefundRowsAsync(db, decided, ct));
    }

    static async Task<AdminRefundRow[]> RefundRowsAsync(AppDb db, IReadOnlyCollection<Refund> rows, CancellationToken ct)
    {
        var reservationIds = rows.Select(r => r.ReservationId).Distinct().ToArray();
        var reservations = reservationIds.Length == 0 ? [] : await db.Reservations.Find(r => reservationIds.Contains(r.Id)).ToListAsync(ct);
        var reservationsById = reservations.ToDictionary(r => r.Id);
        var lotIds = reservations.Select(r => r.LotId).Distinct().ToArray();
        var lots = lotIds.Length == 0 ? [] : await db.Lots.Find(l => lotIds.Contains(l.Id)).Project(l => new { l.Id, l.Name }).ToListAsync(ct);
        var lotNames = lots.ToDictionary(x => x.Id, x => x.Name);
        var spotIds = reservations.Select(r => r.SpotId).Distinct().ToArray();
        var spots = spotIds.Length == 0 ? [] : await db.Spots.Find(s => spotIds.Contains(s.Id)).ToListAsync(ct);
        var labels = spots.ToDictionary(s => s.Id, s => s.Label);
        var userIds = rows.Select(r => r.RequestedByUserId).Distinct().ToArray();
        var users = userIds.Length == 0 ? [] : await db.Users.Find(u => userIds.Contains(u.Id)).Project(u => new { u.Id, u.Name }).ToListAsync(ct);
        var userNames = users.ToDictionary(x => x.Id, x => x.Name);
        return rows.Select(r =>
        {
            var reservation = reservationsById.GetValueOrDefault(r.ReservationId);
            return new AdminRefundRow(
                r.Id, r.ReservationId, r.PaymentId, r.Rule, r.AmountCents, r.Reason, r.CreatedAt,
                r.DecisionNote, r.ApprovedByUserId, r.PaidAt,
                r.Status.ToString().ToLowerInvariant(),
                reservation is null ? null : lotNames.GetValueOrDefault(reservation.LotId),
                reservation is null ? null : labels.GetValueOrDefault(reservation.SpotId),
                userNames.GetValueOrDefault(r.RequestedByUserId),
                reservation?.TotalCents ?? 0);
        }).ToArray();
    }

    /// <summary>Approve a requested refund at (or below) the paid cap.</summary>
    public static async Task<Outcome> ApproveRefundAsync(Guid id, RefundDecisionInput input, Guid adminId, AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        var refund = await db.Refunds.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (refund is null)
            return Outcome.Missing();
        var payment = await db.Payments.Find(p => p.Id == refund.PaymentId).FirstOrDefaultAsync(ct);
        var cap = payment?.AmountCents ?? refund.AmountCents;
        if (input.AmountCents < 0 || input.AmountCents > cap)
            return Outcome.Fail("amountCents", $"Refunds cannot exceed the paid amount (${cap / 100.0:F2}).");
        var approved = await db.Refunds.UpdateOneAsync(
            r => r.Id == id && r.Status == RefundStatus.Requested,
            Builders<Refund>.Update
                .Set(r => r.AmountCents, input.AmountCents)
                .Set(r => r.Status, RefundStatus.Approved)
                .Set(r => r.DecisionNote, input.Note)
                .Set(r => r.ApprovedByUserId, adminId),
            cancellationToken: ct);
        if (approved.ModifiedCount == 0)
            return Outcome.FlashBack(new Toast("info", "That refund was already decided."));
        await realtime.BoardChangedAsync("refund", ct);
        return Outcome.FlashBack(new Toast("success", "Refund approved. Pay it to complete the flow."));
    }

    /// <summary>Pay an approved refund and mark the source payment refunded.</summary>
    public static async Task<Outcome> PayRefundAsync(Guid id, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var refund = await db.Refunds.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (refund is null)
            return Outcome.Missing();
        var paid = await db.Refunds.UpdateOneAsync(
            r => r.Id == id && r.Status == RefundStatus.Approved,
            Builders<Refund>.Update
                .Set(r => r.Status, RefundStatus.Paid)
                .Set(r => r.PaidAt, DateTime.UtcNow),
            cancellationToken: ct);
        if (paid.ModifiedCount == 0)
            return Outcome.FlashBack(new Toast("info", "Only approved refunds can be paid."));
        await db.Payments.UpdateOneAsync(
            p => p.Id == refund.PaymentId,
            Builders<Payment>.Update.Set(p => p.Status, PaymentStatus.Refunded), cancellationToken: ct);
        // RefundPaidEmail handler respects the user's emailOnRefund preference.
        await jobs.EnqueueAsync(db, new RefundPaidEmail(id), ct: ct);
        jobs.Wake();
        await realtime.BoardChangedAsync("refund", ct);
        return Outcome.FlashBack(new Toast("success", "Refund marked paid."));
    }

    /// <summary>Reject a requested refund with the decision note.</summary>
    public static async Task<Outcome> RejectRefundAsync(Guid id, RefundDecisionInput input, AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        var rejected = await db.Refunds.UpdateOneAsync(
            r => r.Id == id && r.Status == RefundStatus.Requested,
            Builders<Refund>.Update
                .Set(r => r.Status, RefundStatus.Rejected)
                .Set(r => r.DecisionNote, input.Note ?? "Rejected"),
            cancellationToken: ct);
        if (rejected.ModifiedCount == 0)
            return Outcome.FlashBack(new Toast("info", "That refund was already decided."));
        await realtime.BoardChangedAsync("refund", ct);
        return Outcome.FlashBack(new Toast("success", "Refund rejected."));
    }

    // ---------- insurance & users ----------

    /// <summary>Spot Cover policies + claims with user/lot labels for the admin insurance page.</summary>
    public static async Task<AdminInsurancePage> InsuranceAsync(AppDb db, CancellationToken ct)
    {
        var policies = await db.SpotCoverPolicies.Find(_ => true).SortByDescending(p => p.CreatedAt).Limit(100).ToListAsync(ct);
        var claims = await db.PolicyClaims.Find(_ => true).SortByDescending(c => c.CreatedAt).Limit(100).ToListAsync(ct);
        var reservationIds = policies.Select(p => p.ReservationId).Distinct().ToArray();
        var reservations = reservationIds.Length == 0 ? [] : await db.Reservations.Find(r => reservationIds.Contains(r.Id)).ToListAsync(ct);
        var reservationById = reservations.ToDictionary(r => r.Id);
        var lotIds = reservations.Select(r => r.LotId).Distinct().ToArray();
        var lots = lotIds.Length == 0 ? [] : await db.Lots.Find(l => lotIds.Contains(l.Id)).Project(l => new { l.Id, l.Name }).ToListAsync(ct);
        var lotNames = lots.ToDictionary(x => x.Id, x => x.Name);
        var userIds = policies.Select(p => p.UserId).Concat(claims.Select(c => c.UserId)).Distinct().ToArray();
        var users = userIds.Length == 0 ? [] : await db.Users.Find(u => userIds.Contains(u.Id)).Project(u => new { u.Id, u.Name, u.Email }).ToListAsync(ct);
        var usersById = users.ToDictionary(u => u.Id);
        return new AdminInsurancePage(
            policies.Select(p =>
            {
                var user = usersById.GetValueOrDefault(p.UserId);
                return new AdminPolicyRow(
                    p.Id, p.ReservationId, p.PremiumCents, p.CoverageCents, p.StartedAt,
                    p.Status.ToString().ToLowerInvariant(),
                    lotNames.GetValueOrDefault(p.LotId),
                    user?.Name, user?.Email);
            }).ToArray(),
            claims.Select(c => new AdminClaimRow(
                c.Id, c.PolicyId, c.Description, c.ClaimedCents, c.PayoutCents, c.IncidentOn, c.DecisionNote,
                c.Kind.ToString().ToLowerInvariant(),
                c.Status.ToString().ToLowerInvariant(),
                usersById.GetValueOrDefault(c.UserId)?.Name)).ToArray());
    }

    /// <summary>Most recent 200 users with roles for the admin users table.</summary>
    public static async Task<AdminUserRow[]> UsersAsync(AppDb db, CancellationToken ct)
    {
        var rows = await db.Users.Find(_ => true).SortBy(u => u.CreatedAt).Limit(200).ToListAsync(ct);
        return rows.Select(u => new AdminUserRow(
            u.Id, u.Name, u.Email, u.EmailVerifiedAt, u.CreatedAt,
            u.Role.ToString().ToLowerInvariant())).ToArray();
    }

    /// <summary>Toggle a user's email verification flag.</summary>
    public static async Task<Outcome> ToggleVerifyAsync(Guid id, AppDb db, CancellationToken ct)
    {
        var user = await db.Users.Find(u => u.Id == id).FirstOrDefaultAsync(ct);
        if (user is null)
            return Outcome.Missing();
        if (user.EmailVerifiedAt is null)
            await db.Users.UpdateOneAsync(u => u.Id == id && u.EmailVerifiedAt == null,
                Builders<User>.Update.Set(u => u.EmailVerifiedAt, DateTime.UtcNow), cancellationToken: ct);
        else
            await db.Users.UpdateOneAsync(u => u.Id == id,
                Builders<User>.Update.Set(u => u.EmailVerifiedAt, null), cancellationToken: ct);
        return Outcome.FlashBack(new Toast("success", user.EmailVerifiedAt is null ? "Email marked verified." : "Verification revoked."));
    }
}

public sealed record AdminOverview(
    long RefundsPending, long ClaimsPending, int OverdueSessions, long OpenIncidents, long NoShowsToday,
    long ReservationsToday, long SessionsToday, long RevenueTodayCents, LotCard[] Lots);

internal sealed record AdminLotDetail(LotCard Lot, AdminLotSpot[] Spots, BookingView[] Upcoming);

public sealed record AdminLotSpot(Guid Id, string Label, int Level, DateTime? LastCheckpointAt, string? CheckpointNote, string Type, string State);

internal sealed record AdminBookingRow(BookingView View, string? UserName, string? UserEmail);

internal sealed record AdminBookingDetail(BookingView View, AdminUserRef? User);

public sealed record AdminUserRef(Guid Id, string Name, string Email);

public sealed record AdminSessionRow(Guid Id, string PlateNo, DateTime EnteredAtUtc, DateTime? ExitedAtUtc, string? CancelReason, string Status, string? LotName, string? SpotLabel);

public sealed record AdminSessionsPage(AdminSessionRow[] Open, AdminSessionRow[] Exited);

public sealed record AdminRefundRow(
    Guid Id, Guid ReservationId, Guid PaymentId, string Rule, long AmountCents, string Reason, DateTime CreatedAt,
    string? DecisionNote, Guid? ApprovedByUserId, DateTime? PaidAt, string Status,
    string? LotName, string? SpotLabel, string? RequestedByName, long MaxCents);

public sealed record AdminRefundsPage(AdminRefundRow[] Pending, AdminRefundRow[] Decided);

public sealed record AdminPolicyRow(
    Guid Id, Guid ReservationId, long PremiumCents, long CoverageCents, DateTime StartedAt, string Status,
    string? LotName, string? UserName, string? UserEmail);

public sealed record AdminClaimRow(
    Guid Id, Guid PolicyId, string Description, long ClaimedCents, long? PayoutCents, DateTime IncidentOn,
    string? DecisionNote, string Kind, string Status, string? UserName);

public sealed record AdminInsurancePage(AdminPolicyRow[] Policies, AdminClaimRow[] Claims);

public sealed record AdminUserRow(Guid Id, string Name, string Email, DateTime? EmailVerifiedAt, DateTime CreatedAt, string Role);
