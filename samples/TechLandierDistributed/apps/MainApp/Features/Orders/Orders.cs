using System.Security.Claims;
using TechLandier.Infrastructure;

using TechLandierDistributed.MainApp;

namespace TechLandier.Features;

public static class Orders
{
    public static void MapOrders(this IEndpointRouteBuilder app)
    {
        // Public tracking: an unguessable token, or the signed-in owner.
        app.MapGet("/orders/{number}", Track);

        var account = app.MapGroup("/account/orders").RequireAuthorization("verified");
        account.MapGet("/", async (ClaimsPrincipal user, OrderService orders, CancellationToken ct) =>
            Render("account/Orders", new InertiaProps { ["orders"] = await orders.AccountOrdersAsync(user.UserId(), ct) }));
        account.MapGet("/{number}", AccountOrder);

        var admin = app.MapGroup("/admin/orders").RequireAuthorization("admin");
        admin.MapGet("/", AdminOrders);
        admin.MapGet("/{number}", AdminOrder);
        admin.MapPost("/{number}/status", async (string number, MoveOrderInput input, ClaimsPrincipal user, OrderService orders, CancellationToken ct) =>
            await orders.MoveAsync(number, input, user.UserId(), ct)).WithInertiaValidation();
        admin.MapPost("/{number}/refund", async (string number, RefundInput input, ClaimsPrincipal user, OrderService orders, CancellationToken ct) =>
            await orders.RefundAsync(number, input, user.UserId(), ct)).WithInertiaValidation();
        admin.MapPost("/payments/{id:guid}/settle", async (Guid id, ClaimsPrincipal user, OrderService orders, CancellationToken ct) =>
            await orders.SettlePaymentAsync(id, user.UserId(), ct));
    }

    // The order is checked first; its timeline, delivery and plan are independent queries that load together.
    // events/delivery listen on the order's public channel so tracking pages refresh while open; the worker
    // publishes the same event after the commit that changed it.
    static async Task<IResult> Track(string number, string? t, ClaimsPrincipal user, OrderService orders, CancellationToken ct)
    {
        var (order, forbidden) = await orders.TrackedOrderAsync(number, t, user.UserIdOrNull(), ct);
        if (forbidden) return RouteResults.Forbidden();
        if (order is null) return RouteResults.NotFoundPage();
        var timeline = LiveEvents.Timeline(number);
        return Render("orders/Track", new InertiaProps
        {
            ["token"] = t,
            ["order"] = order,
            ["events"] = Prop(async c => await orders.EventsAsync(order.Id, c)).Parallel().Live(timeline),
            ["delivery"] = Prop(async c => await orders.DeliveryAsync(order.Id, c)).Parallel().Live(timeline),
        });
    }

    static async Task<IResult> AccountOrder(string number, ClaimsPrincipal user, OrderService orders, CancellationToken ct)
    {
        var uid = user.UserId();
        var order = await orders.AccountOrderAsync(number, uid, ct);
        if (order is null) return RouteResults.NotFoundPage();
        var timeline = LiveEvents.Timeline(number);
        return Render("account/Order", new InertiaProps
        {
            ["order"] = order,
            ["events"] = Prop(async c => await orders.EventsAsync(order.Id, c)).Parallel().Live(timeline),
            ["delivery"] = Prop(async c => await orders.DeliveryAsync(order.Id, c)).Parallel().Live(timeline),
            ["plan"] = Prop(async c => await orders.PlanAsync(order.Id, c)).Parallel().Live(timeline),
            ["warranties"] = Prop(async c => await orders.WarrantiesAsync(order.Id, c)).Parallel(),
            ["insurances"] = Prop(async c => await orders.InsurancesAsync(order.Id, c)).Parallel(),
            ["reviewSlugs"] = Prop(async c => await orders.ReviewCtasAsync(order.Id, uid, c)).Parallel(),
        });
    }

    static async Task<IResult> AdminOrders(string? status, string? q, int? page, OrderService orders, CancellationToken ct) =>
        Render("admin/Orders", new InertiaProps
        {
            ["orders"] = Live(await orders.AdminOrdersAsync(status, q, page, ct), LiveEvents.AdminPulse()),
            ["statusFilter"] = status ?? "",
            ["q"] = q ?? "",
        });

    static async Task<IResult> AdminOrder(string number, OrderService orders, CancellationToken ct)
    {
        var order = await orders.AdminOrderAsync(number, ct);
        if (order is null) return RouteResults.NotFoundPage();
        return Render("admin/Order", new InertiaProps
        {
            ["order"] = Live(order, LiveEvents.AdminPulse()),
            ["events"] = Prop(async c => await orders.EventsAsync(order.Id, c)).Parallel().Live(LiveEvents.AdminPulse()),
            ["delivery"] = Prop(async c => await orders.DeliveryAsync(order.Id, c)).Parallel().Live(LiveEvents.AdminPulse()),
            ["customer"] = Prop(async c => await orders.CustomerAsync(order.Id, c)).Parallel(),
            ["payments"] = Prop(async c => await orders.PaymentsAsync(order.Id, c)).Parallel().Live(LiveEvents.AdminPulse()),
            ["plan"] = Prop(async c => await orders.PlanAsync(order.Id, c)).Parallel().Live(LiveEvents.AdminPulse()),
            ["activeRiders"] = Prop(orders.ActiveRidersAsync).Parallel(),
            ["warranties"] = Prop(async c => await orders.WarrantiesAsync(order.Id, c)).Parallel(),
            ["insurances"] = Prop(async c => await orders.InsurancesAsync(order.Id, c)).Parallel(),
        });
    }
}
