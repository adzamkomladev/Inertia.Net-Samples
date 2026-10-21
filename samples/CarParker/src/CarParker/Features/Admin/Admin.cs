using System.Security.Claims;
using CarParker.Features.Analytics;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
using CarParker.Features.Insurance;
using CarParker.Features.Lots;
using CarParker.Infrastructure;

namespace CarParker.Features.Admin;

/// <summary>Admin console: overview, lots and spot overrides, bookings, sessions, refunds, insurance, incidents, users, analytics.</summary>
public static class Admin
{
    static readonly TimeSpan AnalyticsTtl = TimeSpan.FromSeconds(60);

    public static void MapAdmin(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireAuthorization("admin");
        admin.MapGet("/", OverviewAsync);
        admin.MapGet("/lots", async (AdminService svc, CancellationToken ct) =>
            Render("admin/Lots", new InertiaProps { ["lots"] = await svc.LotsAsync(ct) }));
        admin.MapPost("/lots", CreateLotAsync).WithInertiaPrecognition().RequireRateLimiting("writes");
        admin.MapGet("/lots/{id:guid}", LotAsync);
        admin.MapPut("/lots/{id:guid}", async (Guid id, LotInput input, AdminService svc, HttpContext ctx, CancellationToken ct) =>
            (await svc.UpdateLotAsync(id, input, ct)).ToResult(ctx)).WithInertiaPrecognition().RequireRateLimiting("writes");
        admin.MapPost("/lots/{id:guid}/spots/{spotId:guid}/override", async (Guid id, Guid spotId, SpotOverrideInput input, AdminService svc, HttpContext ctx, CancellationToken ct) =>
            (await svc.OverrideSpotAsync(id, spotId, input, ct)).ToResult(ctx)).RequireRateLimiting("writes");

        admin.MapGet("/bookings", BookingsAsync);
        admin.MapGet("/bookings/{id:guid}", BookingAsync);
        admin.MapPost("/bookings/{id:guid}/cancel", async (Guid id, BookingsService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.CancelAsync(id, user.UserId(), isAdmin: true, ct)).ToResult(ctx));
        admin.MapGet("/sessions", SessionsAsync);
        admin.MapPost("/sessions/{id:guid}/force-close", async (Guid id, string? note, GuardService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.ForceCloseAsync(id, note, user.UserId(), ct)).ToResult(ctx));

        admin.MapGet("/refunds", RefundsAsync);
        admin.MapPost("/refunds/{id:guid}/approve", async (Guid id, RefundDecisionInput input, AdminService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.ApproveRefundAsync(id, input, user.UserId(), ct)).ToResult(ctx));
        admin.MapPost("/refunds/{id:guid}/pay", async (Guid id, AdminService svc, HttpContext ctx, CancellationToken ct) =>
            (await svc.PayRefundAsync(id, ct)).ToResult(ctx));
        admin.MapPost("/refunds/{id:guid}/reject", async (Guid id, RefundDecisionInput input, AdminService svc, HttpContext ctx, CancellationToken ct) =>
            (await svc.RejectRefundAsync(id, input, ct)).ToResult(ctx));

        admin.MapGet("/insurance", async (InsuranceService svc, CancellationToken ct) =>
        {
            var page = await svc.AdminPageAsync(ct);
            return Render("admin/Insurance", new InertiaProps { ["policies"] = page.Policies, ["claims"] = page.Claims });
        });
        admin.MapPost("/claims/{id:guid}/approve", async (Guid id, ClaimDecisionInput input, InsuranceService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.ApproveClaimAsync(id, input, user.UserId(), ct)).ToResult(ctx));
        admin.MapPost("/claims/{id:guid}/pay", async (Guid id, InsuranceService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.PayClaimAsync(id, user.UserId(), ct)).ToResult(ctx));
        admin.MapPost("/claims/{id:guid}/reject", async (Guid id, ClaimDecisionInput input, InsuranceService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.RejectClaimAsync(id, input, user.UserId(), ct)).ToResult(ctx));

        admin.MapGet("/incidents", IncidentsAsync);
        admin.MapGet("/users", async (AdminService svc, CancellationToken ct) =>
            Render("admin/Users", new InertiaProps { ["users"] = await svc.UsersAsync(ct) }));
        admin.MapPost("/users/{id:guid}/verify", async (Guid id, AdminService svc, HttpContext ctx, CancellationToken ct) =>
            (await svc.ToggleVerifyAsync(id, ct)).ToResult(ctx));
        admin.MapGet("/analytics", AnalyticsAsync);
    }

    static async Task<IResult> OverviewAsync(AdminService svc, CancellationToken ct)
    {
        var data = await svc.OverviewAsync(ct);
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
                today = new { reservations = data.ReservationsToday, sessions = data.SessionsToday, revenueCents = data.RevenueTodayCents },
                lots = data.Lots,
            },
        });
    }

    static async Task<IResult> CreateLotAsync(LotInput input, AdminService svc, HttpContext ctx, CancellationToken ct)
    {
        var lot = await svc.CreateLotAsync(input, ct);
        ctx.Inertia().Flash("toast", new Toast("success", $"Lot {lot.Name} created with a fresh A–D spot grid."));
        return Results.Redirect($"/admin/lots/{lot.Id}");
    }

    static async Task<IResult> LotAsync(Guid id, AdminService svc, CancellationToken ct)
    {
        var data = await svc.LotAsync(id, ct);
        return data is null
            ? HttpExtensions.NotFoundPage()
            : Render("admin/Lot", new InertiaProps { ["lot"] = data.Lot, ["spots"] = data.Spots, ["upcoming"] = data.Upcoming });
    }

    static async Task<IResult> BookingsAsync(string? status, AdminService svc, CancellationToken ct)
    {
        var rows = await svc.BookingsAsync(status, ct);
        return Render("admin/Bookings", new InertiaProps
        {
            ["bookings"] = rows.Select(r => new
            {
                r.View.Id, r.View.LotName, r.View.SpotLabel, r.View.Status, r.View.TotalCents, r.View.WindowType,
                r.View.StartAtUtc, r.View.EndAtUtc, r.View.Note, r.View.PlateNo, r.View.Occurrences,
                userName = r.UserName, userEmail = r.UserEmail,
            }),
            ["statusFilter"] = status,
        });
    }

    static async Task<IResult> BookingAsync(Guid id, AdminService svc, CancellationToken ct)
    {
        var data = await svc.BookingAsync(id, ct);
        if (data is null) return HttpExtensions.NotFoundPage();
        var view = data.View;
        return Render("admin/Booking", new InertiaProps
        {
            ["booking"] = new
            {
                view.Id, view.LotName, view.SpotLabel, view.Status, view.WindowType, view.TotalCents, view.ParkingCents,
                view.InsuranceCents, view.CreatedAt, view.CancelledAt, view.Occurrences, view.Pattern, view.PlateNo, view.OccurrenceCount,
                user = data.User,
            },
        });
    }

    static async Task<IResult> SessionsAsync(AdminService svc, CancellationToken ct)
    {
        var data = await svc.SessionsAsync(ct);
        return Render("admin/Sessions", new InertiaProps { ["openSessions"] = data.Open, ["exitedSessions"] = data.Exited });
    }

    static async Task<IResult> RefundsAsync(AdminService svc, CancellationToken ct)
    {
        var data = await svc.RefundsAsync(ct);
        return Render("admin/Refunds", new InertiaProps { ["pending"] = data.Pending, ["decided"] = data.Decided });
    }

    static async Task<IResult> IncidentsAsync(Guid? lot, string? status, GuardService svc, CancellationToken ct)
    {
        var page = await svc.IncidentsPageAsync(lot, status, ct);
        return Render("admin/Incidents", new InertiaProps
        {
            ["lots"] = page.Lots,
            ["filterLot"] = lot,
            ["filterStatus"] = status,
            ["incidents"] = page.Incidents,
            ["kinds"] = GuardService.IncidentKinds,
        });
    }

    /// <summary>The four analytics props are independent reads (the Mongo driver is thread-safe), so each group loads in parallel
    /// and is cached for a minute. Keys carry every input: lot, range and prop.</summary>
    static async Task<IResult> AnalyticsAsync(Guid? lot, int? days, LotsService lots, LotAnalyticsService analytics, CancellationToken ct)
    {
        var range = days is 7 or 30 or 90 ? days.Value : 30;
        var lotId = lot ?? Guid.Empty;
        string Key(string prop) => $"analytics:{lotId}:{range}:{prop}";
        return Render("admin/Analytics", new InertiaProps
        {
            ["days"] = range,
            ["lotFilter"] = lotId,
            ["lots"] = LotsService.OptionsFor(await lots.ActiveAsync(ct)),
            ["kpis"] = Defer(c => analytics.KpisAsync(lotId, range, c), "kpis").Parallel().Cache(Key("kpis"), AnalyticsTtl),
            ["series"] = Defer(c => analytics.SeriesAsync(lotId, range, c), "charts").Parallel().Cache(Key("series"), AnalyticsTtl),
            ["topLots"] = Defer(analytics.TopLotsAsync, "charts").Parallel().Cache(Key("topLots"), AnalyticsTtl),
            ["refundMix"] = Defer(analytics.RefundMixAsync, "charts").Parallel().Cache(Key("refunds"), AnalyticsTtl),
        });
    }
}
