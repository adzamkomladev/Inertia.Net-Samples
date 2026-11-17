using System.Security.Claims;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Orders
{
    public static void MapOrders(this IEndpointRouteBuilder app)
    {
        // Public tracking: an unguessable token, or the signed-in owner.
        app.MapGet("/orders/{number}", Track).WithName("orders.track");

        var account = app.MapGroup("/account/orders").RequireAuthorization("verified").RequireRateLimiting("writes");
        account.MapGet("/", async (ClaimsPrincipal user, OrderService orders, CancellationToken ct) =>
            Render("account/Orders", new InertiaProps { ["orders"] = await orders.AccountOrdersAsync(user.UserId(), ct) })).WithName("account.orders.index");
        account.MapGet("/{number}", AccountOrder).WithName("account.orders.show");

        var admin = app.MapGroup("/admin/orders").RequireAuthorization("admin").RequireRateLimiting("writes");
        admin.MapGet("/", AdminOrders).WithName("admin.orders.index");
        admin.MapGet("/{number}", AdminOrder).WithName("admin.orders.show");
        admin.MapPost("/{number}/status", async (string number, MoveOrderInput input, ClaimsPrincipal user, OrderService orders, CancellationToken ct) =>
            await orders.MoveAsync(number, input, user.UserId(), ct)).WithInertiaValidation().WithName("admin.orders.status");
        admin.MapPost("/{number}/refund", async (string number, RefundInput input, ClaimsPrincipal user, OrderService orders, CancellationToken ct) =>
            await orders.RefundAsync(number, input, user.UserId(), ct)).WithInertiaValidation().WithName("admin.orders.refund");
        admin.MapPost("/payments/{id:guid}/settle", async (Guid id, ClaimsPrincipal user, OrderService orders, CancellationToken ct) =>
            await orders.SettlePaymentAsync(id, user.UserId(), ct)).WithName("admin.payments.settle");
    }

    // The order is checked first; its timeline, delivery and plan are independent queries that load together.
    // events/delivery listen on the order's public channel so tracking pages refresh while open.
    static async Task<IResult> Track(string number, string? t, ClaimsPrincipal user, OrderService orders, CancellationToken ct)
    {
        var (order, forbidden) = await orders.TrackedOrderAsync(number, t, user.UserIdOrNull(), ct);
        if (forbidden) return HttpExtensions.Forbidden();
        if (order is null) return HttpExtensions.NotFoundPage();
        var timeline = OrderEvents.Timeline(number);
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
        if (order is null) return HttpExtensions.NotFoundPage();
        var timeline = OrderEvents.Timeline(number);
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

    static IResult AdminOrders(string? status, string? q, PageRequest page, OrderService orders, CancellationToken ct) =>
        Render("admin/Orders", new InertiaProps
        {
            // Lazy, so a Pulse reloads only this page query instead of re-running the whole handler.
            ["orders"] = Prop(async c => await orders.AdminOrdersAsync(status, q, page, c)).Live(AdminEvents.Pulse()),
            ["statusFilter"] = status ?? "",
            ["q"] = q ?? "",
        });

    static async Task<IResult> AdminOrder(string number, OrderService orders, CancellationToken ct)
    {
        // A cheap id lookup answers 404 and keys the independent props; the order itself is a lazy live prop.
        if (await orders.AdminOrderIdAsync(number, ct) is not { } orderId) return HttpExtensions.NotFoundPage();
        return Render("admin/Order", new InertiaProps
        {
            ["order"] = Prop(async c => await orders.AdminOrderAsync(number, c)).Live(AdminEvents.Pulse()),
            ["events"] = Prop(async c => await orders.EventsAsync(orderId, c)).Parallel().Live(AdminEvents.Pulse()),
            ["delivery"] = Prop(async c => await orders.DeliveryAsync(orderId, c)).Parallel().Live(AdminEvents.Pulse()),
            ["customer"] = Prop(async c => await orders.CustomerAsync(orderId, c)).Parallel(),
            ["payments"] = Prop(async c => await orders.PaymentsAsync(orderId, c)).Parallel().Live(AdminEvents.Pulse()),
            ["plan"] = Prop(async c => await orders.PlanAsync(orderId, c)).Parallel().Live(AdminEvents.Pulse()),
            ["activeRiders"] = Prop(orders.ActiveRidersAsync).Parallel(),
            ["warranties"] = Prop(async c => await orders.WarrantiesAsync(orderId, c)).Parallel(),
            ["insurances"] = Prop(async c => await orders.InsurancesAsync(orderId, c)).Parallel(),
        });
    }
}
