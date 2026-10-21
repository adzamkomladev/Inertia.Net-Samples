using Inertia.Net;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;
using CarParker.Features.Analytics;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
using CarParker.Features.Incidents;
using CarParker.Features.Insurance;
using CarParker.Features.Lots;
using BookingsApi = CarParker.Features.Bookings.Bookings;

namespace CarParker.Features.Admin;

/// <summary>Admin console: overview, lots &amp; spot overrides, bookings, sessions, refunds, insurance, users, analytics.</summary>
public static class Admin
{
    public static void MapAdmin(this IEndpointRouteBuilder app)
    {
        var staff = app.MapGroup("/admin").RequireAuthorization("admin");
        staff.MapGet("/", OverviewAsync);
        staff.MapGet("/lots", LotsAsync);
        staff.MapPost("/lots", CreateLotAsync).WithInertiaPrecognition().RequireRateLimiting("writes");
        staff.MapGet("/lots/{id:guid}", LotAsync);
        staff.MapPut("/lots/{id:guid}", UpdateLotAsync).WithInertiaPrecognition().RequireRateLimiting("writes");
        staff.MapPost("/lots/{id:guid}/spots/{spotId:guid}/override", OverrideSpotAsync).RequireRateLimiting("writes");
        staff.MapGet("/bookings", BookingsListAsync);
        staff.MapGet("/bookings/{id:guid}", BookingAsync);
        staff.MapPost("/bookings/{id:guid}/cancel", CancelBookingAsync);
        staff.MapGet("/sessions", SessionsAsync);
        staff.MapPost("/sessions/{id:guid}/force-close", ForceCloseAsync);
        staff.MapGet("/refunds", RefundsAsync);
        staff.MapPost("/refunds/{id:guid}/approve", ApproveRefundAsync);
        staff.MapPost("/refunds/{id:guid}/pay", PayRefundAsync);
        staff.MapPost("/refunds/{id:guid}/reject", RejectRefundAsync);
        staff.MapGet("/insurance", InsuranceAsync);
        staff.MapPost("/claims/{id:guid}/approve", ApproveClaimAsync);
        staff.MapPost("/claims/{id:guid}/pay", PayClaimAsync);
        staff.MapPost("/claims/{id:guid}/reject", RejectClaimAsync);
        staff.MapGet("/incidents", IncidentsAsync);
        staff.MapGet("/users", UsersAsync);
        staff.MapPost("/users/{id:guid}/verify", ToggleVerifyAsync);
        staff.MapGet("/analytics", AnalyticsAsync).RequireAuthorization("admin");
    }

    /// <summary>GET /admin — KPI cards + needs-attention counts with links.</summary>
    static async Task<IResult> OverviewAsync(AppDb db, CancellationToken ct)
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
        return Render("admin/Index", new InertiaProps
        {
            ["overview"] = new
            {
                needsAttention = new
                {
                    refundsPending, claimsPending,
                    overdueSessions = overdue.Count,
                    openIncidents = openIncidnts,
                    noShowsToday,
                },
                today = new
                {
                    reservations = reservationsToday,
                    sessions = sessionsToday,
                    revenueCents = revenueToday.Sum(p => p.AmountCents),
                },
                lots = await LotsService.CardsAsync(db, lots, ct),
            },
        });
    }

    static async Task<IResult> LotsAsync(AppDb db, CancellationToken ct)
    {
        var lots = await LotsService.ActiveAsync(db, ct);
        var inactive = await db.Lots.Find(l => !l.IsActive).SortBy(l => l.Name).ToListAsync(ct);
        return Render("admin/Lots", new InertiaProps
        {
            ["lots"] = await LotsService.CardsAsync(db, [.. lots, .. inactive], ct),
        });
    }

    static async Task<IResult> CreateLotAsync(LotInput input, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
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
        ctx.Inertia().Flash("toast", new Toast("success", $"Lot {lot.Name} created with a fresh A–D spot grid."));
        return Results.Redirect($"/admin/lots/{lot.Id}");
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

    static async Task<IResult> LotAsync(Guid id, AppDb db, CancellationToken ct)
    {
        var lot = await db.Lots.Find(l => l.Id == id).FirstOrDefaultAsync(ct);
        if (lot is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        var spots = await db.Spots.Find(s => s.LotId == id).SortBy(s => s.Level).ThenBy(s => s.Label).ToListAsync(ct);
        var upcoming = await db.Reservations.Find(r => r.LotId == id && r.Status == ReservationStatus.Confirmed)
            .SortBy(r => r.CreatedAt).Limit(50).ToListAsync(ct);
        return Render("admin/Lot", new InertiaProps
        {
            ["lot"] = (await LotsService.CardsAsync(db, [lot], ct))[0],
            ["spots"] = spots.Select(s => new
            {
                s.Id, s.Label, s.Level, s.LastCheckpointAt, s.CheckpointNote,
                type = s.Type.ToString().ToLowerInvariant(),
                state = s.State.ToString().ToLowerInvariant(),
            }),
            ["upcoming"] = (await BookingsApi.BookingViewsAsync(db, upcoming, ct)).Take(10),
        });
    }

    static async Task<IResult> UpdateLotAsync(Guid id, LotInput input, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var lot = await db.Lots.Find(l => l.Id == id).FirstOrDefaultAsync(ct);
        if (lot is null)
            return Back().WithFlash("toast", new Toast("error", "That lot no longer exists."));
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
            return Back().WithFlash("toast", new Toast("error", "That lot no longer exists."));
        await SearchIndexing.RequestAsync(db, jobs, id, Math.Max(1, lot.IndexVersion) + 1, ct);
        await realtime.LotChangedAsync(id, ct);
        return Back().WithFlash("toast", new Toast("success", "Lot updated."));
    }

    /// <summary>Manual override: block (state=blocked with note) or clear (block a live session? no — clear only frees a free-ish spot).</summary>
    static async Task<IResult> OverrideSpotAsync(Guid id, Guid spotId, SpotOverrideInput input, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        var spot = await db.Spots.Find(s => s.Id == spotId && s.LotId == id && s.Active).FirstOrDefaultAsync(ct);
        if (spot is null)
            return Back().WithFlash("toast", new Toast("error", "Spot not found."));
        var now = DateTime.UtcNow;
        switch ((input.Action ?? "").ToLowerInvariant())
        {
            case "block":
                if (spot.State == SpotState.Occupied)
                    return Back().WithFlash("toast", new Toast("error", "A session is open on that spot. Check the car out first."));
                await db.Spots.UpdateOneAsync(
                    s => s.Id == spotId && s.State != SpotState.Occupied,
                    Builders<Spot>.Update
                        .Set(s => s.State, SpotState.Blocked)
                        .Set(s => s.LastCheckpointAt, now)
                        .Set(s => s.CheckpointNote, input.Note ?? "Blocked by admin"),
                    cancellationToken: ct);
                await realtime.SpotChangedAsync(id, spotId, "blocked", ct);
                return Back().WithFlash("toast", new Toast("success", $"{spot.Label} blocked."));
            case "clear":
                await db.Spots.UpdateOneAsync(
                    s => s.Id == spotId && s.State == SpotState.Blocked,
                    Builders<Spot>.Update
                        .Set(s => s.State, SpotState.Free)
                        .Set(s => s.LastCheckpointAt, now)
                        .Set(s => s.CheckpointNote, input.Note ?? "Cleared by admin"),
                    cancellationToken: ct);
                await realtime.SpotChangedAsync(id, spotId, "free", ct);
                return Back().WithFlash("toast", new Toast("success", $"{spot.Label} cleared."));
            default:
                return Back().WithErrors(new Dictionary<string, string> { ["action"] = "Use block or clear." });
        }
    }

    static async Task<IResult> BookingsListAsync(string? status, AppDb db, CancellationToken ct)
    {
        var filter = Builders<Reservation>.Filter.Empty;
        if (status is { Length: > 0 } && Enum.TryParse<ReservationStatus>(status, ignoreCase: true, out var wanted))
            filter &= Builders<Reservation>.Filter.Eq(r => r.Status, wanted);
        var rows = await db.Reservations.Find(filter).SortByDescending(r => r.CreatedAt).Limit(100).ToListAsync(ct);
        var userIds = rows.Select(r => r.UserId).Distinct().ToArray();
        var users = userIds.Length == 0 ? [] : await db.Users.Find(u => userIds.Contains(u.Id))
            .Project(u => new { u.Id, u.Name, u.Email }).ToListAsync(ct);
        var usersById = users.ToDictionary(u => u.Id);
        var views = await BookingsApi.BookingViewsAsync(db, rows, ct);
        return Render("admin/Bookings", new InertiaProps
        {
            ["bookings"] = views.Zip(rows).Select(pair =>
            {
                var (view, reservation) = pair;
                return new
                {
                    view.Id, view.LotName, view.SpotLabel, view.Status, view.TotalCents, view.WindowType,
                    view.StartAtUtc, view.EndAtUtc, view.Note, view.PlateNo, view.Occurrences,
                    userName = usersById.GetValueOrDefault(reservation.UserId)?.Name,
                    userEmail = usersById.GetValueOrDefault(reservation.UserId)?.Email,
                };
            }),
            ["statusFilter"] = status,
        });
    }

    static async Task<IResult> BookingAsync(Guid id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var reservation = await db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (reservation is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        var view = (await BookingsApi.BookingViewsAsync(db, [reservation], ct))[0];
        var user = await db.Users.Find(u => u.Id == reservation.UserId).Project(u => new { u.Id, u.Name, u.Email }).FirstOrDefaultAsync(ct);
        return Render("admin/Booking", new InertiaProps
        {
            ["booking"] = new
            {
                view.Id, view.LotName, view.SpotLabel, view.Status, view.WindowType, view.TotalCents,
                view.ParkingCents, view.InsuranceCents, view.CreatedAt, view.CancelledAt, view.Occurrences,
                view.Pattern, view.PlateNo, view.OccurrenceCount,
                user = user is null ? null : new { user.Id, user.Name, user.Email },
            },
        });
    }

    static async Task<IResult> CancelBookingAsync(Guid id, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        // BookingsService.CancelAsync accepts admin overrides through the ownership check.
        return Respond(ctx, await BookingsService.CancelAsync(id, ctx.User.UserId(), db,
            ctx.RequestServices.GetRequiredService<JobQueue>(), realtime, ct));
    }

    /// <summary>Maps a service outcome onto the Inertia contract: 404 page, 403, back+errors, flash-then-redirect, or back+flash.</summary>
    static IResult Respond(HttpContext ctx, Outcome outcome)
    {
        if (outcome.NotFound)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (outcome.Forbidden)
            return HttpExtensions.Forbidden();
        if (outcome.Errors is { } errors)
            return Back().WithErrors(errors);
        if (outcome.Redirect is { } url)
        {
            if (outcome.Flash is { } flash) ctx.Inertia().Flash("toast", flash);
            return Results.Redirect(url);
        }
        return outcome.Flash is { } toast ? Back().WithFlash("toast", toast) : Back();
    }

    static async Task<IResult> SessionsAsync(AppDb db, CancellationToken ct)
    {
        var open = await db.ParkingSessions.Find(s => s.Status == SessionStatus.InProgress).SortBy(s => s.EnteredAtUtc).ToListAsync(ct);
        var exited = await db.ParkingSessions.Find(s => s.Status == SessionStatus.Exited).SortByDescending(s => s.ExitedAtUtc).Limit(100).ToListAsync(ct);
        var lotIds = open.Concat(exited).Select(s => s.LotId).Distinct().ToArray();
        var lotNames = lotIds.Length == 0 ? [] : await db.Lots.Find(l => lotIds.Contains(l.Id)).Project(l => new { l.Id, l.Name }).ToListAsync(ct);
        var namesById = lotNames.ToDictionary(x => x.Id, x => x.Name);
        var spotIds = open.Concat(exited).Select(s => s.SpotId).Distinct().ToArray();
        var spots = spotIds.Length == 0 ? [] : await db.Spots.Find(s => spotIds.Contains(s.Id)).ToListAsync(ct);
        var labelsById = spots.ToDictionary(s => s.Id, s => s.Label);
        object Row(ParkingSession s) => new
        {
            s.Id, s.PlateNo, s.EnteredAtUtc, s.ExitedAtUtc, s.CancelReason,
            status = s.Status.ToString().ToLowerInvariant(),
            lotName = namesById.GetValueOrDefault(s.LotId),
            spotLabel = labelsById.GetValueOrDefault(s.SpotId),
        };
        return Render("admin/Sessions", new InertiaProps
        {
            ["openSessions"] = open.Select(Row),
            ["exitedSessions"] = exited.Select(Row),
        });
    }

    static async Task<IResult> ForceCloseAsync(Guid id, HttpContext ctx, AppDb db, RealtimeEvents realtime, JobQueue jobs, string? note, CancellationToken ct)
    {
        var session = await db.ParkingSessions.Find(s => s.Id == id).FirstOrDefaultAsync(ct);
        if (session is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (session.Status != SessionStatus.InProgress)
            return Back().WithFlash("toast", new Toast("info", "That session already ended."));
        var closed = await GuardService.CloseSessionAsync(db, jobs, session, ctx.User.UserId(), "force_close" + (string.IsNullOrWhiteSpace(note) ? "" : $": {note}"), realtime, ct);
        if (closed)
        {
            await realtime.SpotChangedAsync(session.LotId, session.SpotId, "free", ct);
            await realtime.BoardChangedAsync("session", ct);
        }
        return Back().WithFlash("toast", new Toast("success", "Session force-closed."));
    }

    static async Task<IResult> RefundsAsync(AppDb db, CancellationToken ct)
    {
        var pending = await db.Refunds.Find(r => r.Status == RefundStatus.Requested).SortBy(r => r.CreatedAt).ToListAsync(ct);
        var decided = await db.Refunds.Find(r => r.Status != RefundStatus.Requested).SortByDescending(r => r.CreatedAt).Limit(100).ToListAsync(ct);
        return Render("admin/Refunds", new InertiaProps
        {
            ["pending"] = await RefundRowsAsync(db, pending, ct),
            ["decided"] = await RefundRowsAsync(db, decided, ct),
        });
    }

    static async Task<List<object>> RefundRowsAsync(AppDb db, IReadOnlyCollection<Refund> rows, CancellationToken ct)
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
            return (object)new
            {
                r.Id, r.ReservationId, r.PaymentId, r.Rule, r.AmountCents, r.Reason, r.CreatedAt,
                r.DecisionNote, r.ApprovedByUserId, r.PaidAt,
                status = r.Status.ToString().ToLowerInvariant(),
                lotName = reservation is null ? null : lotNames.GetValueOrDefault(reservation.LotId),
                spotLabel = reservation is null ? null : labels.GetValueOrDefault(reservation.SpotId),
                requestedByName = userNames.GetValueOrDefault(r.RequestedByUserId),
                maxCents = reservation?.TotalCents ?? 0,
            };
        }).ToList();
    }

    static async Task<IResult> ApproveRefundAsync(Guid id, RefundDecisionInput input, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        var refund = await db.Refunds.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (refund is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        var payment = await db.Payments.Find(p => p.Id == refund.PaymentId).FirstOrDefaultAsync(ct);
        var cap = payment?.AmountCents ?? refund.AmountCents;
        if (input.AmountCents < 0 || input.AmountCents > cap)
            return Back().WithErrors(new Dictionary<string, string>
            {
                ["amountCents"] = $"Refunds cannot exceed the paid amount (${cap / 100.0:F2}).",
            });
        var approved = await db.Refunds.UpdateOneAsync(
            r => r.Id == id && r.Status == RefundStatus.Requested,
            Builders<Refund>.Update
                .Set(r => r.AmountCents, input.AmountCents)
                .Set(r => r.Status, RefundStatus.Approved)
                .Set(r => r.DecisionNote, input.Note)
                .Set(r => r.ApprovedByUserId, ctx.User.UserId()),
            cancellationToken: ct);
        if (approved.ModifiedCount == 0)
            return Back().WithFlash("toast", new Toast("info", "That refund was already decided."));
        await realtime.BoardChangedAsync("refund", ct);
        return Back().WithFlash("toast", new Toast("success", "Refund approved. Pay it to complete the flow."));
    }

    static async Task<IResult> PayRefundAsync(Guid id, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var refund = await db.Refunds.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (refund is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        var paid = await db.Refunds.UpdateOneAsync(
            r => r.Id == id && r.Status == RefundStatus.Approved,
            Builders<Refund>.Update
                .Set(r => r.Status, RefundStatus.Paid)
                .Set(r => r.PaidAt, DateTime.UtcNow),
            cancellationToken: ct);
        if (paid.ModifiedCount == 0)
            return Back().WithFlash("toast", new Toast("info", "Only approved refunds can be paid."));
        await db.Payments.UpdateOneAsync(
            p => p.Id == refund.PaymentId,
            Builders<Payment>.Update.Set(p => p.Status, PaymentStatus.Refunded), cancellationToken: ct);
        // RefundPaidEmail handler respects the user's emailOnRefund preference.
        await jobs.EnqueueAsync(db, new RefundPaidEmail(id), ct: ct);
        jobs.Wake();
        await realtime.BoardChangedAsync("refund", ct);
        return Back().WithFlash("toast", new Toast("success", "Refund marked paid."));
    }

    static async Task<IResult> RejectRefundAsync(Guid id, RefundDecisionInput input, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        var rejected = await db.Refunds.UpdateOneAsync(
            r => r.Id == id && r.Status == RefundStatus.Requested,
            Builders<Refund>.Update
                .Set(r => r.Status, RefundStatus.Rejected)
                .Set(r => r.DecisionNote, input.Note ?? "Rejected"),
            cancellationToken: ct);
        if (rejected.ModifiedCount == 0)
            return Back().WithFlash("toast", new Toast("info", "That refund was already decided."));
        await realtime.BoardChangedAsync("refund", ct);
        return Back().WithFlash("toast", new Toast("success", "Refund rejected."));
    }

    static async Task<IResult> InsuranceAsync(AppDb db, CancellationToken ct)
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
        return Render("admin/Insurance", new InertiaProps
        {
            ["policies"] = policies.Select(p =>
            {
                var reservation = reservationById.GetValueOrDefault(p.ReservationId);
                return new
                {
                    p.Id, p.ReservationId, p.PremiumCents, p.CoverageCents, p.StartedAt,
                    status = p.Status.ToString().ToLowerInvariant(),
                    lotName = lotNames.GetValueOrDefault(p.LotId),
                    userName = usersById.GetValueOrDefault(p.UserId)?.Name,
                    userEmail = usersById.GetValueOrDefault(p.UserId)?.Email,
                };
            }),
            ["claims"] = claims.Select(c => new
            {
                c.Id, c.PolicyId, c.Description, c.ClaimedCents, c.PayoutCents, c.IncidentOn, c.DecisionNote,
                kind = c.Kind.ToString().ToLowerInvariant(),
                status = c.Status.ToString().ToLowerInvariant(),
                userName = usersById.GetValueOrDefault(c.UserId)?.Name,
            }),
        });
    }

    static async Task<IResult> ApproveClaimAsync(Guid id, ClaimDecisionInput input, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        var ok = await InsuranceService.ApproveAsync(db, id, input.PayoutCents, ctx.User.UserId(), input.Note, ct);
        if (!ok) return Back().WithFlash("toast", new Toast("info", "Only submitted claims can be approved."));
        await realtime.BoardChangedAsync("claim", ct);
        return Back().WithFlash("toast", new Toast("success", "Claim approved — pay it to notify the customer."));
    }

    static async Task<IResult> PayClaimAsync(Guid id, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var ok = await InsuranceService.PayClaimAsync(db, jobs, id, ctx.User.UserId(), ct);
        if (!ok) return Back().WithFlash("toast", new Toast("info", "Only approved claims can be paid."));
        await jobs.EnqueueAsync(db, new ClaimDecisionEmail(id, "paid"), ct: ct);
        jobs.Wake();
        await realtime.BoardChangedAsync("claim", ct);
        return Back().WithFlash("toast", new Toast("success", "Claim paid — the customer got the decision email."));
    }

    static async Task<IResult> RejectClaimAsync(Guid id, ClaimDecisionInput input, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var ok = await InsuranceService.RejectAsync(db, id, ctx.User.UserId(), input.Note, ct);
        if (!ok) return Back().WithFlash("toast", new Toast("info", "That claim was already decided."));
        await jobs.EnqueueAsync(db, new ClaimDecisionEmail(id, "rejected"), ct: ct);
        jobs.Wake();
        await realtime.BoardChangedAsync("claim", ct);
        return Back().WithFlash("toast", new Toast("success", "Claim rejected."));
    }

    static async Task<IResult> IncidentsAsync(Guid? lot, string? status, AppDb db, CancellationToken ct)
    {
        var lots = await LotsService.ActiveAsync(db, ct);
        var rows = await GuardService.IncidentsAsync(db, lot, status, ct);
        var lotNames = lots.ToDictionary(l => l.Id, l => l.Name);
        var reporterIds = rows.Select(i => i.ReporterUserId).Distinct().ToArray();
        var reporters = reporterIds.Length == 0 ? [] : await db.Users.Find(u => reporterIds.Contains(u.Id))
            .Project(u => new { u.Id, u.Name }).ToListAsync(ct);
        var namesById = reporters.ToDictionary(x => x.Id, x => x.Name);
        return Render("admin/Incidents", new InertiaProps
        {
            ["lots"] = LotsService.OptionsFor(lots),
            ["filterLot"] = lot,
            ["filterStatus"] = status,
            ["incidents"] = rows.Select(i => new
            {
                i.Id, i.LotId, i.Kind, i.PlateNo, i.Description, i.Note,
                status = i.Status.ToString().ToLowerInvariant(),
                i.CreatedAt, i.ResolvedAt,
                lotName = lotNames.GetValueOrDefault(i.LotId),
                reporterName = namesById.GetValueOrDefault(i.ReporterUserId),
            }),
            ["kinds"] = GuardService.IncidentKinds,
        });
    }

    static async Task<IResult> UsersAsync(AppDb db, CancellationToken ct)
    {
        var rows = await db.Users.Find(_ => true).SortBy(u => u.CreatedAt).Limit(200).ToListAsync(ct);
        return Render("admin/Users", new InertiaProps
        {
            ["users"] = rows.Select(u => new
            {
                u.Id, u.Name, u.Email, u.EmailVerifiedAt, u.CreatedAt,
                role = u.Role.ToString().ToLowerInvariant(),
            }),
        });
    }

    static async Task<IResult> ToggleVerifyAsync(Guid id, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        var user = await db.Users.Find(u => u.Id == id).FirstOrDefaultAsync(ct);
        if (user is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (user.EmailVerifiedAt is null)
            await db.Users.UpdateOneAsync(u => u.Id == id && u.EmailVerifiedAt == null,
                Builders<User>.Update.Set(u => u.EmailVerifiedAt, DateTime.UtcNow), cancellationToken: ct);
        else
            await db.Users.UpdateOneAsync(u => u.Id == id,
                Builders<User>.Update.Set(u => u.EmailVerifiedAt, null), cancellationToken: ct);
        return Back().WithFlash("toast", new Toast("success", user.EmailVerifiedAt is null ? "Email marked verified." : "Verification revoked."));
    }

    static readonly TimeSpan AnalyticsTtl = TimeSpan.FromSeconds(60);

    /// <summary>GET /admin/analytics — deferred props cached 60s with the key pattern lots:{lotId}:{days}:{prop};
    /// lotId Guid.Empty means "all lots".</summary>
    static async Task<IResult> AnalyticsAsync(Guid? lot, int? days, AppDb db, CancellationToken ct)
    {
        var n = days is 7 or 30 or 90 ? days.Value : 30;
        var lotId = lot is { } pick && pick != Guid.Empty ? pick : Guid.Empty; // Empty = all lots
        var lots = await LotsService.ActiveAsync(db, ct);
        return Render("admin/Analytics", new InertiaProps
        {
            ["days"] = n,
            ["lotFilter"] = lotId,
            ["lots"] = LotsService.OptionsFor(lots),
            ["kpis"] = Defer(async c => await LotAnalytics.KpisAsync(db, lotId, n, c), "kpis")
                .Cache(AnalyticsKey(lotId, n, "kpis"), AnalyticsTtl),
            ["series"] = Defer(async c => await LotAnalytics.SeriesAsync(db, lotId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1 - n), n, c), "charts")
                .Cache(AnalyticsKey(lotId, n, "series"), AnalyticsTtl),
            ["topLots"] = Defer(async c => await LotAnalytics.TopLotsAsync(db, c), "charts")
                .Cache(AnalyticsKey(lotId, n, "topLots"), AnalyticsTtl),
            ["refundMix"] = Defer(async c => await LotAnalytics.RefundMixAsync(db, c), "charts")
                .Cache(AnalyticsKey(lotId, n, "refunds"), AnalyticsTtl),
        });
    }

    static string AnalyticsKey(Guid lotId, int days, string prop) => $"lots:{lotId}:{days}:{prop}";
}
