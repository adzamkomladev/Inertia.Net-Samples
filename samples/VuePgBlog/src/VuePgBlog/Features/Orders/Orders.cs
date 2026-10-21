using VuePgBlog.Data;
using VuePgBlog.Infrastructure;

namespace VuePgBlog.Features;

public static class Orders
{
    public static void MapOrders(this IEndpointRouteBuilder app)
    {
        // Public tracking with an unguessable token (or owner); redirects guests to login at /login when no token.
        app.MapGet("/orders/{number}", TrackAsync);
    }

    static IResult NotFoundPage() => Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);

    /// Guests follow /orders/{number}?t={token}; owners load it signed-in without a token.
    internal static async Task<IResult> TrackAsync(string number, string? t, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var lookup = await OrderService.LookupForTrackAsync(number, t, ctx.User.UserIdOrNull(), db, ct);
        if (lookup.NotFound) return NotFoundPage();
        if (lookup.Forbidden) return HttpExtensions.Forbidden(); // signed in, not the owner
        return Render("orders/Track", new InertiaProps
        {
            ["token"] = t,
            ["order"] = await OrderService.OrderView(db, lookup.OrderId, ct),
            ["events"] = await OrderService.TransitionEvents(db, lookup.OrderId, ct),
            ["delivery"] = await OrderService.DeliveryView(db, lookup.OrderId, ct),
        });
    }
}

public static class AdminOrders
{
    public static void MapAdminOrders(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin/orders").RequireAuthorization("admin");
        admin.MapGet("/", AccountOrders.AdminOrdersAsync);
        admin.MapGet("/{number}", AccountOrders.AdminOrderAsync);
        admin.MapPost("/{number}/status", (string number, string? to, string? note, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct) =>
            AccountOrders.MoveAsync(number, to, note, ctx, db, jobs, realtime, ct)).WithInertiaValidation();
        admin.MapPost("/{number}/refund", (string number, string? note, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct) =>
            AccountOrders.RefundAsync(number, note, ctx, db, jobs, realtime, ct)).WithInertiaValidation();
        admin.MapPost("/payments/{id:guid}/settle", (Guid id, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct) =>
            AccountOrders.SettlePaymentAsync(id, ctx, db, jobs, realtime, ct));
    }
}

public static class AccountOrders
{
    public static void MapAccountOrders(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account/orders").RequireAuthorization("verified");
        group.MapGet("/", AccountOrdersAsync);
        group.MapGet("/{number}", AccountOrderAsync);
    }

    static IResult NotFoundPage() => Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);

    static IResult Finish(SaveResult r) => r switch
    {
        { NotFound: true } => NotFoundPage(),
        { Errors: { } errors } => Back().WithErrors(errors),
        { FlashType: { } type, FlashMessage: { } message } => Back().WithFlash("toast", new Toast(type, message)),
        _ => Back(),
    };

    internal static async Task<IResult> AccountOrdersAsync(HttpContext ctx, AppDb db, CancellationToken ct) =>
        Render("account/Orders", new InertiaProps
        {
            ["orders"] = await OrderService.GetAccountOrdersAsync(ctx.User.UserId(), db, ct),
        });

    internal static async Task<IResult> AccountOrderAsync(string number, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var data = await OrderService.GetAccountOrderAsync(number, ctx.User.UserId(), db, ct);
        return data is null
            ? NotFoundPage()
            : Render("account/Order", new InertiaProps
            {
                ["order"] = data.Order,
                ["events"] = data.Events,
                ["delivery"] = data.Delivery,
                ["plan"] = data.Plan,
                ["warranties"] = data.Warranties,
                ["insurances"] = data.Insurances,
                ["reviewSlugs"] = data.ReviewSlugs,
            });
    }

    internal static async Task<IResult> AdminOrdersAsync(string? status, string? q, int? page, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var (orders, statusFilter, query) = await OrderService.GetAdminOrdersAsync(status, q, page, db, ct);
        return Render("admin/Orders", new InertiaProps
        {
            ["orders"] = orders,
            ["statusFilter"] = statusFilter,
            ["q"] = query,
        });
    }

    internal static async Task<IResult> AdminOrderAsync(string number, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var data = await OrderService.GetAdminOrderAsync(number, db, ct);
        return data is null
            ? NotFoundPage()
            : Render("admin/Order", new InertiaProps
            {
                ["order"] = data.Order,
                ["events"] = data.Events,
                ["delivery"] = data.Delivery,
                ["customer"] = data.Customer,
                ["payments"] = data.Payments,
                ["plan"] = data.Plan,
                ["activeRiders"] = data.ActiveRiders,
            });
    }

    internal static async Task<IResult> MoveAsync(string number, string? to, string? note, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct) =>
        Finish(await OrderService.MoveAsync(number, to, note, ctx.User.UserId(), db, jobs, realtime, ct));

    internal static async Task<IResult> RefundAsync(string number, string? note, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct) =>
        Finish(await OrderService.RefundAsync(number, note, ctx.User.UserId(), db, jobs, realtime, ct));

    internal static async Task<IResult> SettlePaymentAsync(Guid id, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct) =>
        Finish(await OrderService.SettlePaymentAsync(id, ctx.User.UserId(), db, jobs, realtime, ct));
}
