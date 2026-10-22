using System.Security.Claims;
using CarParker.Features.Alerts;
using CarParker.Features.Insurance;
using CarParker.Infrastructure;

namespace CarParker.Features.Bookings;

/// <summary>Customer console: /account overview, bookings, payments, insurance and alerts.</summary>
public static class Bookings
{
    public static void MapBookings(this IEndpointRouteBuilder app)
    {
        app.MapPost("/notifications/read-all", async (AlertsService svc, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await svc.MarkAllReadAsync(user.UserId(), ct);
            return Back();
        }).RequireAuthorization();
        var account = app.MapGroup("/account").RequireAuthorization("verified");
        account.MapGet("/", AccountAsync);
        account.MapGet("/bookings", async (BookingsService svc, ClaimsPrincipal user, CancellationToken ct) =>
            Render("account/Bookings", new InertiaProps { ["bookings"] = await svc.ListAsync(user.UserId(), ct) }));
        account.MapPost("/bookings", async (BookingInput input, BookingsService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
                (await svc.CreateAsync(input, user.UserId(), ct)).ToResult(ctx))
            .WithInertiaPrecognition()
            .RequireRateLimiting("writes");
        account.MapGet("/bookings/{id:guid}", BookingAsync);
        account.MapPost("/bookings/{id:guid}/cancel", async (Guid id, BookingsService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.CancelAsync(id, user.UserId(), user.IsAdmin(), ct)).ToResult(ctx));
        account.MapPost("/bookings/{id:guid}/pay", async (Guid id, PayInput input, BookingsService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.PayAsync(id, input, user.UserId(), user.IsAdmin(), ct)).ToResult(ctx));
        account.MapGet("/payments", PaymentsAsync);
        account.MapGet("/insurance", async (InsuranceService svc, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var page = await svc.CustomerPageAsync(user.UserId(), ct);
            return Render("account/Insurance", new InertiaProps { ["policies"] = page.Policies, ["claims"] = page.Claims });
        });
        account.MapPost("/insurance/{policyId:guid}/claims", async (Guid policyId, ClaimInput input, InsuranceService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.CreateClaimAsync(policyId, input, user.UserId(), ct)).ToResult(ctx));
        account.MapGet("/alerts", async (AlertsService svc, ClaimsPrincipal user, CancellationToken ct) =>
            Render("account/Alerts", new InertiaProps { ["alerts"] = (await svc.ListAsync(user.UserId(), ct)).Select(AlertsMappers.Row) }));
        account.MapPost("/alerts/read", async (Guid[] ids, AlertsService svc, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await svc.MarkReadAsync(user.UserId(), ids, ct);
            return Back().WithFlash("toast", new Toast("success", "All caught up."));
        });
    }

    static async Task<IResult> AccountAsync(BookingsService svc, ClaimsPrincipal user, CancellationToken ct)
    {
        var data = await svc.AccountAsync(user.UserId(), ct);
        return Render("account/Index", new InertiaProps { ["account"] = data, ["lots"] = data.Lots });
    }

    static async Task<IResult> PaymentsAsync(BookingsService svc, ClaimsPrincipal user, CancellationToken ct)
    {
        var page = await svc.PaymentsAsync(user.UserId(), ct);
        return Render("account/Payments", new InertiaProps { ["payments"] = page.Payments, ["refunds"] = page.Refunds });
    }

    static async Task<IResult> BookingAsync(Guid id, BookingsService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct)
    {
        var (detail, failure) = await svc.DetailAsync(id, user.UserId(), user.IsAdmin(), ct);
        if (detail is null) return failure!.ToResult(ctx);
        return Render("account/Booking", new InertiaProps
        {
            ["booking"] = detail.View,
            ["payment"] = detail.Payment,
            ["refund"] = detail.Refund,
            ["sessions"] = detail.Sessions,
            ["policy"] = detail.Policy,
        });
    }
}
