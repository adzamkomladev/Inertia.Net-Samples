using TechLandier.Data;
using TechLandier.Infrastructure;
namespace TechLandier.Features;

public static class Deliveries
{
    public static void MapDeliveries(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireAuthorization("admin");
        var board = admin.MapGroup("/deliveries");
        board.MapGet("/", BoardAsync);
        board.MapPost("/{id:guid}/assign", AssignAsync);
        board.MapPost("/{id:guid}/pickup", PickupAsync);
        board.MapPost("/{id:guid}/deliver", DeliverAsync);
        board.MapPost("/{id:guid}/fail", FailAsync);
        admin.MapPost("/riders", CreateRiderAsync);
        admin.MapPost("/riders/{id:guid}", UpdateRiderAsync);
        admin.MapPost("/zones", CreateZoneAsync);
        admin.MapPost("/zones/{id:guid}", UpdateZoneAsync);
    }

    static IResult Finish(SaveResult r) => r switch
    {
        { NotFound: true } => Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404),
        { Errors: { } errors } => Back().WithErrors(errors),
        { FlashType: { } type, FlashMessage: { } message } => Back().WithFlash("toast", new Toast(type, message)),
        _ => Back(),
    };

    internal static async Task<IResult> BoardAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var data = await DeliveriesService.GetBoardAsync(db, ct);
        return Render("admin/Deliveries", new InertiaProps
        {
            ["deliveries"] = data.Deliveries,
            ["riders"] = data.Riders,
            ["zones"] = data.Zones,
        });
    }

    internal static async Task<IResult> AssignAsync(Guid id, Guid? riderId, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        Finish(await DeliveriesService.AssignRiderAsync(id, riderId, db, realtime, ct));

    internal static async Task<IResult> PickupAsync(Guid id, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        Finish(await DeliveriesService.PickupAsync(id, db, realtime, ct));

    internal static async Task<IResult> DeliverAsync(Guid id, decimal? cash, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct) =>
        Finish(await DeliveriesService.DeliverAsync(id, cash, db, jobs, realtime, ct));

    internal static async Task<IResult> FailAsync(Guid id, string? reason, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        Finish(await DeliveriesService.FailAsync(id, reason, db, realtime, ct));

    internal static async Task<IResult> CreateRiderAsync(string? name, string? phone, string? vehicle, string? service, HttpContext ctx, AppDb db, CancellationToken ct) =>
        Finish(await DeliveriesService.CreateRiderAsync(name, phone, vehicle, service, db, ct));

    internal static async Task<IResult> UpdateRiderAsync(Guid id, string? name, string? phone, string? vehicle, string? service, bool? isActive, HttpContext ctx, AppDb db, CancellationToken ct) =>
        Finish(await DeliveriesService.UpdateRiderAsync(id, name, phone, vehicle, service, isActive, db, ct));

    internal static async Task<IResult> CreateZoneAsync(string? name, decimal? fee, string? etaText, bool? allowPayOnDelivery, HttpContext ctx, AppDb db, CancellationToken ct) =>
        Finish(await DeliveriesService.CreateZoneAsync(name, fee, etaText, allowPayOnDelivery, db, ct));

    internal static async Task<IResult> UpdateZoneAsync(Guid id, string? name, decimal? fee, string? etaText, bool? allowPayOnDelivery, bool? isActive, HttpContext ctx, AppDb db, CancellationToken ct) =>
        Finish(await DeliveriesService.UpdateZoneAsync(id, name, fee, etaText, allowPayOnDelivery, isActive, db, ct));
}
