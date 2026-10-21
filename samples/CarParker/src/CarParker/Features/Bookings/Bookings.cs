using System.Security.Claims;
using Inertia.Net;
using Microsoft.AspNetCore.Mvc;
using CarParker.Data;
using CarParker.Infrastructure;
using CarParker.Features.Alerts;

namespace CarParker.Features.Bookings;

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
        var data = await BookingsService.AccountAsync(ctx.User.UserId(), db, ct);
        var next = data.NextVisit;
        var session = data.ActiveSession;
        return Render("account/Index", new InertiaProps
        {
            ["account"] = new
            {
                upcomingCount = data.UpcomingCount,
                nextVisit = next is null ? null : new
                {
                    next.Id, next.StartAtUtc, next.EndAtUtc,
                    spotLabel = next.SpotLabel, lotName = next.LotName, bookingId = next.BookingId,
                },
                upcoming = data.Upcoming.Select(u => new
                {
                    u.Id, u.TotalCents, window = u.Window, u.StartAtUtc, u.EndAtUtc, u.OccurrenceCount,
                    lotName = u.LotName, spotLabel = u.SpotLabel,
                }),
                activeSession = session is null ? null : new
                {
                    session.Id, session.EnteredAtUtc,
                    lotName = session.LotName, spotLabel = session.SpotLabel,
                },
                spend30dCents = data.Spend30dCents,
                unreadAlerts = data.UnreadAlerts,
            },
            ["lots"] = data.Lots,
        });
    }

    /// <summary>GET /account/bookings — flat list, most recent first, with lot/spot labels for cards.</summary>
    static async Task<IResult> BookingsListAsync(HttpContext ctx, AppDb db, CancellationToken ct) =>
        Render("account/Bookings", new InertiaProps { ["bookings"] = await BookingsService.ListAsync(ctx.User.UserId(), db, ct) });

    static async Task<IResult> BookingAsync(Guid id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var lookup = await BookingsService.BookingDetailAsync(id, ctx.User.UserId(), IsStaff(ctx), db, ct);
        if (lookup.NotFound)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (lookup.Forbidden)
            return HttpExtensions.Forbidden();
        var detail = lookup.Detail!;
        var payment = detail.Payment;
        var refund = detail.Refund;
        var policy = detail.Policy;
        return Render("account/Booking", new InertiaProps
        {
            ["booking"] = detail.View,
            ["payment"] = payment is null ? null : new
            {
                payment.Id,
                method = payment.Method,
                payment.AmountCents,
                status = payment.Status,
                payment.PaidAt,
            },
            ["refund"] = refund is null ? null : new
            {
                refund.Id, refund.Rule, refund.AmountCents,
                status = refund.Status, refund.CreatedAt,
            },
            ["sessions"] = detail.Sessions.Select(s => new
            {
                s.Id, s.SpotId, s.PlateNo, s.EnteredAtUtc, s.ExitedAtUtc,
                status = s.Status,
            }),
            ["policy"] = policy is null ? null : new
            {
                policy.Id, policy.PremiumCents, policy.CoverageCents,
                status = policy.Status,
                claims = policy.Claims.Select(c => new
                {
                    c.Id,
                    kind = c.Kind,
                    c.ClaimedCents, c.PayoutCents,
                    status = c.Status,
                    c.CreatedAt, c.DecisionNote,
                }),
            },
        });
    }

    static bool IsStaff(HttpContext ctx) =>
        string.Equals(ctx.User.FindFirstValue(AuthHelpers.RoleClaim) ?? "", "admin", StringComparison.OrdinalIgnoreCase);

    static async Task<IResult> CreateAsync(BookingInput input, HttpContext ctx, AppDb db, RealtimeEvents realtime,
        CancellationToken ct) =>
        Respond(ctx, await BookingsService.CreateAsync(input, ctx.User.UserId(), db, realtime, ct));

    static async Task<IResult> CancelAsync(Guid id, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        Respond(ctx, await BookingsService.CancelAsync(id, ctx.User.UserId(), db,
            ctx.RequestServices.GetRequiredService<JobQueue>(), realtime, ct));

    static async Task<IResult> PayAsync(Guid id, PayInput input, HttpContext ctx, AppDb db, CancellationToken ct) =>
        Respond(ctx, await BookingsService.PayAsync(id, input, ctx.User.UserId(), db, ct));

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

    /// <summary>GET /account/payments — payments + refunds list with booking labels.</summary>
    static async Task<IResult> PaymentsAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var data = await BookingsService.PaymentsAsync(ctx.User.UserId(), db, ct);
        return Render("account/Payments", new InertiaProps
        {
            ["payments"] = data.Payments.Select(p => new
            {
                p.Id, p.ReservationId, p.AmountCents,
                method = p.Method,
                status = p.Status,
                p.PaidAt,
                p.CreatedAt,
                lotName = p.LotName,
                spotLabel = p.SpotLabel,
            }),
            ["refunds"] = data.Refunds.Select(rf => new
            {
                rf.Id, rf.ReservationId, rf.Rule, rf.AmountCents,
                status = rf.Status,
                rf.CreatedAt,
            }),
        });
    }

    static async Task<IResult> InsuranceAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var data = await BookingsService.InsuranceAsync(ctx.User.UserId(), db, ct);
        return Render("account/Insurance", new InertiaProps
        {
            ["policies"] = data.Policies.Select(p => new
            {
                p.Id, p.PremiumCents, p.CoverageCents,
                status = p.Status,
                p.StartedAt, p.CreatedAt,
                reservationId = p.ReservationId,
                lotName = p.LotName,
                reservedThrough = p.ReservedThrough,
            }),
            ["claims"] = data.Claims.Select(c => new
            {
                c.Id, c.PolicyId,
                kind = c.Kind,
                c.Description, c.ClaimedCents, c.PayoutCents,
                status = c.Status,
                c.CreatedAt, c.PaidAt, c.DecisionNote,
            }),
        });
    }

    static async Task<IResult> CreateClaimAsync(Guid policyId, ClaimInput input, HttpContext ctx, AppDb db,
        RealtimeEvents realtime, CancellationToken ct) =>
        Respond(ctx, await BookingsService.CreateClaimAsync(policyId, input, ctx.User.UserId(), db, realtime, ct));

    static async Task<IResult> AlertsAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var rows = await AlertsService.ListAsync(db, ctx.User.UserId(), ct);
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
