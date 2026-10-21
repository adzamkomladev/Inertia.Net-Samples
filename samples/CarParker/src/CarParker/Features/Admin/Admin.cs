using Inertia.Net;
using CarParker.Data;
using CarParker.Infrastructure;
using CarParker.Features.Analytics;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
using CarParker.Features.Insurance;
using CarParker.Features.Lots;

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
        var data = await AdminService.OverviewAsync(db, ct);
        return Render("admin/Index", new InertiaProps
        {
            ["overview"] = new
            {
                needsAttention = new
                {
                    refundsPending = data.RefundsPending,
                    claimsPending = data.ClaimsPending,
                    overdueSessions = data.OverdueSessions,
                    openIncidents = data.OpenIncidents,
                    noShowsToday = data.NoShowsToday,
                },
                today = new
                {
                    reservations = data.ReservationsToday,
                    sessions = data.SessionsToday,
                    revenueCents = data.RevenueTodayCents,
                },
                lots = data.Lots,
            },
        });
    }

    static async Task<IResult> LotsAsync(AppDb db, CancellationToken ct) =>
        Render("admin/Lots", new InertiaProps { ["lots"] = await AdminService.LotsAsync(db, ct) });

    static async Task<IResult> CreateLotAsync(LotInput input, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var lot = await AdminService.CreateLotAsync(input, db, jobs, realtime, ct);
        ctx.Inertia().Flash("toast", new Toast("success", $"Lot {lot.Name} created with a fresh A–D spot grid."));
        return Results.Redirect($"/admin/lots/{lot.Id}");
    }

    static async Task<IResult> LotAsync(Guid id, AppDb db, CancellationToken ct)
    {
        var data = await AdminService.LotAsync(id, db, ct);
        if (data is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        return Render("admin/Lot", new InertiaProps
        {
            ["lot"] = data.Lot,
            ["spots"] = data.Spots.Select(s => new
            {
                s.Id, s.Label, s.Level, s.LastCheckpointAt, s.CheckpointNote,
                type = s.Type,
                state = s.State,
            }),
            ["upcoming"] = data.Upcoming,
        });
    }

    static async Task<IResult> UpdateLotAsync(Guid id, LotInput input, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct) =>
        Respond(ctx, await AdminService.UpdateLotAsync(id, input, db, jobs, realtime, ct));

    /// <summary>Manual override: block (state=blocked with note) or clear (block a live session? no — clear only frees a free-ish spot).</summary>
    static async Task<IResult> OverrideSpotAsync(Guid id, Guid spotId, SpotOverrideInput input, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        Respond(ctx, await AdminService.OverrideSpotAsync(id, spotId, input, db, realtime, ct));

    static async Task<IResult> BookingsListAsync(string? status, AppDb db, CancellationToken ct)
    {
        var rows = await AdminService.BookingsAsync(status, db, ct);
        return Render("admin/Bookings", new InertiaProps
        {
            ["bookings"] = rows.Select(r => new
            {
                r.View.Id, r.View.LotName, r.View.SpotLabel, r.View.Status, r.View.TotalCents, r.View.WindowType,
                r.View.StartAtUtc, r.View.EndAtUtc, r.View.Note, r.View.PlateNo, r.View.Occurrences,
                userName = r.UserName,
                userEmail = r.UserEmail,
            }),
            ["statusFilter"] = status,
        });
    }

    static async Task<IResult> BookingAsync(Guid id, AppDb db, CancellationToken ct)
    {
        var data = await AdminService.BookingAsync(id, db, ct);
        if (data is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        var user = data.User;
        return Render("admin/Booking", new InertiaProps
        {
            ["booking"] = new
            {
                data.View.Id, data.View.LotName, data.View.SpotLabel, data.View.Status, data.View.WindowType, data.View.TotalCents,
                data.View.ParkingCents, data.View.InsuranceCents, data.View.CreatedAt, data.View.CancelledAt, data.View.Occurrences,
                data.View.Pattern, data.View.PlateNo, data.View.OccurrenceCount,
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
        var data = await AdminService.SessionsAsync(db, ct);
        return Render("admin/Sessions", new InertiaProps
        {
            ["openSessions"] = data.Open.Select(SessionRow),
            ["exitedSessions"] = data.Exited.Select(SessionRow),
        });

        static object SessionRow(AdminSessionRow s) => new
        {
            s.Id, s.PlateNo, s.EnteredAtUtc, s.ExitedAtUtc, s.CancelReason,
            status = s.Status,
            lotName = s.LotName,
            spotLabel = s.SpotLabel,
        };
    }

    static async Task<IResult> ForceCloseAsync(Guid id, HttpContext ctx, AppDb db, RealtimeEvents realtime, JobQueue jobs, string? note, CancellationToken ct) =>
        Respond(ctx, await AdminService.ForceCloseAsync(id, ctx.User.UserId(), note, db, jobs, realtime, ct));

    static async Task<IResult> RefundsAsync(AppDb db, CancellationToken ct)
    {
        var data = await AdminService.RefundsAsync(db, ct);
        return Render("admin/Refunds", new InertiaProps
        {
            ["pending"] = data.Pending.Select(RefundRow),
            ["decided"] = data.Decided.Select(RefundRow),
        });

        static object RefundRow(AdminRefundRow r) => new
        {
            r.Id, r.ReservationId, r.PaymentId, r.Rule, r.AmountCents, r.Reason, r.CreatedAt,
            r.DecisionNote, r.ApprovedByUserId, r.PaidAt,
            status = r.Status,
            lotName = r.LotName,
            spotLabel = r.SpotLabel,
            requestedByName = r.RequestedByName,
            maxCents = r.MaxCents,
        };
    }

    static async Task<IResult> ApproveRefundAsync(Guid id, RefundDecisionInput input, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        Respond(ctx, await AdminService.ApproveRefundAsync(id, input, ctx.User.UserId(), db, realtime, ct));

    static async Task<IResult> PayRefundAsync(Guid id, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct) =>
        Respond(ctx, await AdminService.PayRefundAsync(id, db, jobs, realtime, ct));

    static async Task<IResult> RejectRefundAsync(Guid id, RefundDecisionInput input, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        Respond(ctx, await AdminService.RejectRefundAsync(id, input, db, realtime, ct));

    static async Task<IResult> InsuranceAsync(AppDb db, CancellationToken ct)
    {
        var data = await AdminService.InsuranceAsync(db, ct);
        return Render("admin/Insurance", new InertiaProps
        {
            ["policies"] = data.Policies.Select(p => new
            {
                p.Id, p.ReservationId, p.PremiumCents, p.CoverageCents, p.StartedAt,
                status = p.Status,
                lotName = p.LotName,
                userName = p.UserName,
                userEmail = p.UserEmail,
            }),
            ["claims"] = data.Claims.Select(c => new
            {
                c.Id, c.PolicyId, c.Description, c.ClaimedCents, c.PayoutCents, c.IncidentOn, c.DecisionNote,
                kind = c.Kind,
                status = c.Status,
                userName = c.UserName,
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
        var page = await GuardService.IncidentsPageAsync(db, lot, status, ct);
        return Render("admin/Incidents", new InertiaProps
        {
            ["lots"] = page.Lots,
            ["filterLot"] = lot,
            ["filterStatus"] = status,
            ["incidents"] = page.Incidents.Select(i => new
            {
                i.Id, i.LotId, i.Kind, i.PlateNo, i.Description, i.Note,
                status = i.Status,
                i.CreatedAt, i.ResolvedAt,
                lotName = i.LotName,
                reporterName = i.ReporterName,
            }),
            ["kinds"] = GuardService.IncidentKinds,
        });
    }

    static async Task<IResult> UsersAsync(AppDb db, CancellationToken ct)
    {
        var rows = await AdminService.UsersAsync(db, ct);
        return Render("admin/Users", new InertiaProps
        {
            ["users"] = rows.Select(u => new
            {
                u.Id, u.Name, u.Email, u.EmailVerifiedAt, u.CreatedAt,
                role = u.Role,
            }),
        });
    }

    static async Task<IResult> ToggleVerifyAsync(Guid id, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        Respond(ctx, await AdminService.ToggleVerifyAsync(id, db, ct));

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
