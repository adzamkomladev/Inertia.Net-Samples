using System.Security.Claims;
using Inertia.Net;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Infrastructure;
using SvelteMongoBlog.Features.Alerts;
using SvelteMongoBlog.Features.Guard;
using SvelteMongoBlog.Features.Insurance;
using SvelteMongoBlog.Features.Lots;

namespace SvelteMongoBlog.Features.Bookings;

/// <summary>Customer console: /account overview, bookings, payments, insurance, alerts (+ create/cancel/pay actions).</summary>
public static class Bookings
{
    public static void MapBookings(this IEndpointRouteBuilder app)
    {
        app.MapGet("/account", AccountAsync).RequireAuthorization("verified");
        app.MapGet("/account/bookings", BookingsListAsync).RequireAuthorization("verified");
        app.MapPost("/account/bookings", CreateAsync)
            .RequireAuthorization("verified")
            .WithInertiaPrecognition()
            .RequireRateLimiting("writes");
        app.MapGet("/account/bookings/{id:guid}", BookingAsync).RequireAuthorization("verified");
        app.MapPost("/account/bookings/{id:guid}/cancel", CancelAsync).RequireAuthorization("verified");
        app.MapPost("/account/bookings/{id:guid}/pay", PayAsync).RequireAuthorization("verified");
        app.MapGet("/account/payments", PaymentsAsync).RequireAuthorization("verified");
        app.MapGet("/account/insurance", InsuranceAsync).RequireAuthorization("verified");
        app.MapPost("/account/insurance/{policyId:guid}/claims", CreateClaimAsync).RequireAuthorization("verified");
        app.MapGet("/account/alerts", AlertsAsync).RequireAuthorization("verified");
        app.MapPost("/account/alerts/read", MarkAlertsReadAsync).RequireAuthorization("verified");
    }

    static async Task<IResult> AccountAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var uid = ctx.User.UserId();
        var now = DateTime.UtcNow;
        var reservations = await db.Reservations.Find(r => r.UserId == uid).SortByDescending(r => r.CreatedAt).ToListAsync(ct);
        var upcoming = reservations
            .Where(r => r.Status == ReservationStatus.Confirmed && (r.EndAtUtc is not { } end || end >= now))
            .ToList();
        var upcomingIds = upcoming.Select(r => r.Id).ToArray();
        var nextOccurrence = upcomingIds.Length == 0
            ? null
            : await db.Occurrences
                .Find(o => upcomingIds.Contains(o.ReservationId) && o.Status == OccurrenceStatus.Scheduled && o.StartAtUtc > now)
                .SortBy(o => o.StartAtUtc).FirstOrDefaultAsync(ct);
        var reservationsById = reservations.ToDictionary(r => r.Id);
        var spotIds = reservations.Select(r => r.SpotId).Distinct().ToArray();
        var spots = spotIds.Length == 0 ? [] : await db.Spots.Find(s => spotIds.Contains(s.Id)).ToListAsync(ct);
        var spotsById = spots.ToDictionary(s => s.Id);
        var lotIds = reservations.Select(r => r.LotId).Distinct().ToArray();
        var lots = lotIds.Length == 0 ? [] : await db.Lots.Find(l => lotIds.Contains(l.Id)).ToListAsync(ct);
        var lotsById = lots.ToDictionary(l => l.Id);

        var openSessions = spotsById.Count == 0
            ? []
            : (await db.ParkingSessions.Find(s => s.Status == SessionStatus.InProgress).ToListAsync(ct))
                .Where(s => spotsById.ContainsKey(s.SpotId)).ToList();
        var openSession = openSessions
            .OrderByDescending(s => s.EnteredAtUtc).FirstOrDefault();

        var paidThisMonth = await db.Payments.Find(p => p.UserId == uid && p.Status == PaymentStatus.Paid
                && p.PaidAt >= now.AddDays(-30)).ToListAsync(ct);

        return Render("account/Index", new InertiaProps
        {
            ["account"] = new
            {
                upcomingCount = upcoming.Count,
                nextVisit = nextOccurrence is not { } occ ? null :
                    reservationsById.TryGetValue(occ.ReservationId, out var res) ? new
                    {
                        occ.Id,
                        occ.StartAtUtc,
                        occ.EndAtUtc,
                        spotLabel = spotsById.GetValueOrDefault(res.SpotId)?.Label,
                        lotName = lotsById.GetValueOrDefault(res.LotId)?.Name,
                        bookingId = res.Id,
                    } : null,
                upcoming = Upking(upcoming, lotsById, spotsById),
                activeSession = openSession is null ? null : new
                {
                    openSession.Id,
                    openSession.EnteredAtUtc,
                    lotName = lotsById.GetValueOrDefault(openSession.LotId)?.Name,
                    spotLabel = spotsById.GetValueOrDefault(openSession.SpotId)?.Label,
                },
                spend30dCents = paidThisMonth.Sum(p => p.AmountCents),
                unreadAlerts = await AlertsService.CountUnreadAsync(db, uid, ct),
            },
            ["lots"] = LotsService.OptionsFor(lots),
        });
    }

    static object[] Upking(List<Reservation> reservations, Dictionary<Guid, Lot> lotsById, Dictionary<Guid, Spot> spotsById) => reservations.Take(4).Select(r => new
    {
        r.Id,
        r.TotalCents,
        window = r.WindowType.ToString().ToLowerInvariant(),
        r.StartAtUtc,
        r.EndAtUtc,
        r.OccurrenceCount,
        lotName = lotsById.GetValueOrDefault(r.LotId)?.Name,
        spotLabel = spotsById.TryGetValue(r.SpotId, out var sp) ? sp.Label : null,
    }).ToArray();

    /// <summary>GET /account/bookings — flat list, most recent first, with lot/spot labels for cards.</summary>
    static async Task<IResult> BookingsListAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var uid = ctx.User.UserId();
        var rows = await db.Reservations.Find(r => r.UserId == uid).SortByDescending(r => r.CreatedAt).ToListAsync(ct);
        return Render("account/Bookings", new InertiaProps { ["bookings"] = await BookingViewsAsync(db, rows, ct) });
    }

    internal static async Task<BookingView[]> BookingViewsAsync(AppDb db, IReadOnlyCollection<Reservation> rows, CancellationToken ct)
    {
        var spotIds = rows.Select(r => r.SpotId).Distinct().ToArray();
        var lotIds = rows.Select(r => r.LotId).Distinct().ToArray();
        var reservationIds = rows.Select(r => r.Id).ToArray();
        var spots = spotIds.Length == 0 ? [] : await db.Spots.Find(s => spotIds.Contains(s.Id)).ToListAsync(ct);
        var lots = lotIds.Length == 0 ? [] : await db.Lots.Find(l => lotIds.Contains(l.Id)).ToListAsync(ct);
        var occurrences = reservationIds.Length == 0 ? [] : await db.Occurrences
            .Find(o => reservationIds.Contains(o.ReservationId)).SortBy(o => o.Index).ToListAsync(ct);
        var policyIds = rows.Where(r => r.InsuranceId is { }).Select(r => r.InsuranceId!.Value).Distinct().ToArray();
        var policies = policyIds.Length == 0 ? [] : await db.SpotCoverPolicies.Find(p => policyIds.Contains(p.Id)).ToListAsync(ct);
        var spotsById = spots.ToDictionary(s => s.Id);
        var lotsById = lots.ToDictionary(l => l.Id);
        var policiesById = policies.ToDictionary(p => p.Id);
        return rows.Select(r => new BookingView(
            r.Id, r.LotId, r.SpotId,
            lotsById.GetValueOrDefault(r.LotId)?.Name ?? "Lot",
            spotsById.TryGetValue(r.SpotId, out var spot) ? spot.Label : "?",
            r.WindowType.ToString().ToLowerInvariant(),
            r.TotalCents, r.ParkingCents, r.InsuranceCents,
            r.Status.ToString().ToLowerInvariant(), r.CreatedAt, r.CancelledAt, r.StartAtUtc, r.EndAtUtc,
            r.Pattern, r.OccurrenceCount, r.PlateNo,
            r.InsuranceId is { } pid ? policiesById.GetValueOrDefault(pid)?.Status.ToString().ToLowerInvariant() : null,
            occurrences.Where(o => o.ReservationId == r.Id).Select(o => new OccurrenceView(
                o.Id, o.Index, o.StartAtUtc, o.EndAtUtc, o.Status.ToString().ToLowerInvariant())).ToArray(),
            r.Note)).ToArray();
    }

    internal sealed record BookingView(
        Guid Id, Guid LotId, Guid SpotId, string LotName, string SpotLabel, string WindowType,
        long TotalCents, long ParkingCents, long InsuranceCents, string Status, DateTime CreatedAt,
        DateTime? CancelledAt, DateTime? StartAtUtc, DateTime? EndAtUtc, RecurrencePattern? Pattern,
        int OccurrenceCount, string? PlateNo, string? PolicyStatus, OccurrenceView[] Occurrences, string? Note);

    internal sealed record OccurrenceView(Guid Id, int Index, DateTime StartAtUtc, DateTime EndAtUtc, string Status);

    static async Task<IResult> BookingAsync(Guid id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var reservation = await db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (reservation is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (reservation.UserId != ctx.User.UserId() && !IsStaff(ctx))
            return HttpExtensions.Forbidden();
        var view = (await BookingViewsAsync(db, [reservation], ct))[0];
        var payment = await db.Payments.Find(p => p.ReservationId == id).SortByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);
        var refund = await db.Refunds.Find(rf => rf.ReservationId == id).SortByDescending(rf => rf.CreatedAt).FirstOrDefaultAsync(ct);
        var sessions = await db.ParkingSessions.Find(s => s.OccurrenceId != Guid.Empty && view.Occurrences.Select(o => o.Id).Contains(s.OccurrenceId)).ToListAsync(ct);
        var policy = reservation.InsuranceId is { } policyId
            ? await db.SpotCoverPolicies.Find(p => p.Id == policyId).FirstOrDefaultAsync(ct)
            : null;
        var claims = policy is null ? [] : await db.PolicyClaims.Find(c => c.PolicyId == policy.Id).SortByDescending(c => c.CreatedAt).ToListAsync(ct);
        return Render("account/Booking", new InertiaProps
        {
            ["booking"] = view,
            ["payment"] = payment is null ? null : new
            {
                payment.Id,
                method = payment.Method.ToString().ToLowerInvariant(),
                payment.AmountCents,
                status = payment.Status.ToString().ToLowerInvariant(),
                payment.PaidAt,
            },
            ["refund"] = refund is null ? null : new
            {
                refund.Id, refund.Rule, refund.AmountCents,
                status = refund.Status.ToString().ToLowerInvariant(), refund.CreatedAt,
            },
            ["sessions"] = sessions.Select(s => new
            {
                s.Id, s.SpotId, s.PlateNo, s.EnteredAtUtc, s.ExitedAtUtc,
                status = s.Status.ToString().ToLowerInvariant(),
            }),
            ["policy"] = policy is null ? null : new
            {
                policy.Id, policy.PremiumCents, policy.CoverageCents,
                status = policy.Status.ToString().ToLowerInvariant(),
                claims = claims.Select(c => new
                {
                    c.Id,
                    kind = c.Kind.ToString().ToLowerInvariant(),
                    c.ClaimedCents, c.PayoutCents,
                    status = c.Status.ToString().ToLowerInvariant(),
                    c.CreatedAt, c.DecisionNote,
                }),
            },
        });
    }

    static bool IsStaff(HttpContext ctx) =>
        string.Equals(ctx.User.FindFirstValue(AuthHelpers.RoleClaim) ?? "", "admin", StringComparison.OrdinalIgnoreCase);

    static async Task<IResult> CreateAsync(BookingInput input, HttpContext ctx, AppDb db, RealtimeEvents realtime,
        IConfiguration config, CancellationToken ct) => await BookingsService.CreateAsync(input, ctx, db, realtime, config, ct);

    static async Task<IResult> CancelAsync(Guid id, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        await BookingsService.CancelAsync(id, ctx, db, ctx.RequestServices.GetRequiredService<JobQueue>(), realtime, ct);

    static async Task<IResult> PayAsync(Guid id, PayInput input, HttpContext ctx, AppDb db, CancellationToken ct) =>
        await BookingsService.PayAsync(id, input, ctx, db, ct);

    /// <summary>GET /account/payments — payments + refunds list with booking labels.</summary>
    static async Task<IResult> PaymentsAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var uid = ctx.User.UserId();
        var payments = await db.Payments.Find(p => p.UserId == uid).SortByDescending(p => p.CreatedAt).ToListAsync(ct);
        var reservationIds = payments.Select(p => p.ReservationId).Distinct().ToArray();
        var rows = reservationIds.Length == 0 ? [] : await db.Reservations.Find(r => reservationIds.Contains(r.Id)).ToListAsync(ct);
        var rowsById = rows.ToDictionary(r => r.Id);
        var lotIds = rows.Select(r => r.LotId).Distinct().ToArray();
        var lotNames = lotIds.Length == 0 ? [] : await db.Lots.Find(l => lotIds.Contains(l.Id))
            .Project(l => new { l.Id, l.Name }).ToListAsync(ct);
        var lotsById = lotNames.ToDictionary(x => x.Id, x => x.Name);
        var refunds = reservationIds.Length == 0 ? [] : await db.Refunds
            .Find(rf => reservationIds.Contains(rf.ReservationId)).SortByDescending(rf => rf.CreatedAt).ToListAsync(ct);
        var spotIds = rows.Select(r => r.SpotId).Distinct().ToArray();
        var spots = spotIds.Length == 0 ? [] : await db.Spots.Find(s => spotIds.Contains(s.Id)).ToListAsync(ct);
        var spotsById = spots.ToDictionary(s => s.Id);
        return Render("account/Payments", new InertiaProps
        {
            ["payments"] = payments.Select(p =>
            {
                var booking = rowsById.GetValueOrDefault(p.ReservationId);
                return new
                {
                    p.Id, p.ReservationId, p.AmountCents,
                    method = p.Method.ToString().ToLowerInvariant(),
                    status = p.Status.ToString().ToLowerInvariant(),
                    p.PaidAt,
                    p.CreatedAt,
                    lotName = booking is null ? null : lotsById.GetValueOrDefault(booking.LotId),
                    spotLabel = booking is null ? null : spotsById.GetValueOrDefault(booking.SpotId)?.Label,
                };
            }),
            ["refunds"] = refunds.Select(rf => new
            {
                rf.Id, rf.ReservationId, rf.Rule, rf.AmountCents,
                status = rf.Status.ToString().ToLowerInvariant(),
                rf.CreatedAt,
            }),
        });
    }

    static async Task<IResult> InsuranceAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var uid = ctx.User.UserId();
        var policies = await db.SpotCoverPolicies.Find(p => p.UserId == uid).SortByDescending(p => p.CreatedAt).ToListAsync(ct);
        var claims = await db.PolicyClaims.Find(c => c.UserId == uid).SortByDescending(c => c.CreatedAt).ToListAsync(ct);
        var reservationIds = policies.Select(p => p.ReservationId).Distinct().ToArray();
        var rows = reservationIds.Length == 0 ? [] : await db.Reservations.Find(r => reservationIds.Contains(r.Id)).ToListAsync(ct);
        var lotIds = policies.Select(p => p.LotId).Distinct().ToArray();
        var lots = lotIds.Length == 0 ? [] : await db.Lots.Find(l => lotIds.Contains(l.Id)).ToListAsync(ct);
        var lotsById = lots.ToDictionary(l => l.Id);
        return Render("account/Insurance", new InertiaProps
        {
            ["policies"] = policies.Select(p => new
            {
                p.Id, p.PremiumCents, p.CoverageCents,
                status = p.Status.ToString().ToLowerInvariant(),
                p.StartedAt, p.CreatedAt,
                reservationId = p.ReservationId,
                lotName = lotsById.GetValueOrDefault(p.LotId)?.Name,
                reservedThrough = rows.FirstOrDefault(r => r.Id == p.ReservationId)?.EndAtUtc,
            }),
            ["claims"] = claims.Select(c => new
            {
                c.Id, c.PolicyId,
                kind = c.Kind.ToString().ToLowerInvariant(),
                c.Description, c.ClaimedCents, c.PayoutCents,
                status = c.Status.ToString().ToLowerInvariant(),
                c.CreatedAt, c.PaidAt, c.DecisionNote,
            }),
        });
    }

    static async Task<IResult> CreateClaimAsync(Guid policyId, ClaimInput input, HttpContext ctx, AppDb db,
        RealtimeEvents realtime, CancellationToken ct)
    {
        var uid = ctx.User.UserId();
        var policy = await db.SpotCoverPolicies.Find(p => p.Id == policyId).FirstOrDefaultAsync(ct);
        if (policy is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (policy.UserId != uid)
            return HttpExtensions.Forbidden();
        if (!Enum.TryParse<ClaimKind>(input.Kind, ignoreCase: true, out var kind))
            return Back().WithErrors(new Dictionary<string, string> { ["kind"] = "Choose a claim type." });
        if (policy.Status != PolicyStatus.Active)
            return Back().WithFlash("toast", new Toast("error", "This cover is not active."));
        if (input.ClaimedCents > policy.CoverageCents)
            return Back().WithErrors(new Dictionary<string, string>
            {
                ["claimedCents"] = $"Claims are capped at the policy coverage (${policy.CoverageCents / 100.0:F2}).",
            });
        var claim = await InsuranceService.CreateClaimAsync(db, policy, kind, input.Description!.Trim(), input.IncidentOn ?? DateTime.UtcNow, input.ClaimedCents, ct);
        await AlertsService.StaffPushAsync(realtime, $"New Spot Cover claim ({kind})", "/admin/insurance", ct);
        await realtime.BoardChangedAsync("claim", ct);
        return Back().WithFlash("toast", new Toast("success", $"Claim {claim.Id.ToString()[..8]} submitted."));
    }

    static async Task<IResult> AlertsAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var uid = ctx.User.UserId();
        var rows = await db.Alerts.Find(a => a.UserId == uid).SortByDescending(a => a.At).Limit(100).ToListAsync(ct);
        return Render("account/Alerts", new InertiaProps
        {
            ["alerts"] = rows.Select(a => new
            {
                a.Id, a.Kind, a.Title, a.Body, a.Href, a.At, a.ReadAt,
            }),
        });
    }

    static async Task<IResult> MarkAlertsReadAsync(Guid[] ids, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        await AlertsService.MarkReadAsync(db, ctx.User.UserId(), ids, ct);
        return Back().WithFlash("toast", new Toast("success", "All caught up."));
    }

    internal static string ToPascal(this string value) =>
        string.IsNullOrEmpty(value) ? value : value[..1].ToUpperInvariant() + value[1..];
}
