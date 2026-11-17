using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Deliveries
{
    public static void MapDeliveries(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireAuthorization("admin").RequireRateLimiting("writes");

        var board = admin.MapGroup("/deliveries");
        // Every prop is lazy and independent: they load in parallel, and a Pulse reloads each through its own query.
        board.MapGet("/", (PageRequest page, DeliveriesService svc) =>
            Render("admin/Deliveries", new InertiaProps
            {
                ["deliveries"] = Prop(async c => await svc.OpenDeliveriesAsync(c)).Parallel().Live(AdminEvents.Pulse()),
                ["delivered"] = Prop(async c => await svc.DeliveredAsync(page, c)).Parallel().Live(AdminEvents.Pulse()),
                ["deliveredToday"] = Prop(async c => await svc.DeliveredTodayAsync(c)).Parallel().Live(AdminEvents.Pulse()),
                ["riders"] = Prop(async c => await svc.RidersAsync(c)).Parallel().Live(AdminEvents.Pulse()),
                ["zones"] = Prop(async c => await svc.ZonesAsync(c)).Parallel().Live(AdminEvents.Pulse()),
            })).WithName("admin.deliveries");
        board.MapPost("/{id:guid}/assign", async (Guid id, AssignInput input, DeliveriesService svc, CancellationToken ct) => await svc.AssignRiderAsync(id, input, ct)).WithName("admin.deliveries.assign");
        board.MapPost("/{id:guid}/pickup", async (Guid id, DeliveriesService svc, CancellationToken ct) => await svc.PickupAsync(id, ct)).WithName("admin.deliveries.pickup");
        board.MapPost("/{id:guid}/deliver", async (Guid id, DeliverInput input, DeliveriesService svc, CancellationToken ct) => await svc.DeliverAsync(id, input, ct)).WithName("admin.deliveries.deliver");
        board.MapPost("/{id:guid}/note", async (Guid id, NoteInput input, DeliveriesService svc, CancellationToken ct) => await svc.NoteAsync(id, input, ct)).WithName("admin.deliveries.note");
        board.MapPost("/{id:guid}/fail", async (Guid id, FailInput input, DeliveriesService svc, CancellationToken ct) => await svc.FailAsync(id, input, ct)).WithName("admin.deliveries.fail");

        admin.MapPost("/riders", async (RiderInput input, DeliveriesService svc, CancellationToken ct) => await svc.CreateRiderAsync(input, ct)).WithName("admin.riders.store");
        admin.MapPost("/riders/{id:guid}", async (Guid id, RiderInput input, DeliveriesService svc, CancellationToken ct) => await svc.UpdateRiderAsync(id, input, ct)).WithName("admin.riders.update");
        admin.MapPost("/zones", async (ZoneInput input, DeliveriesService svc, CancellationToken ct) => await svc.CreateZoneAsync(input, ct)).WithName("admin.zones.store");
        admin.MapPost("/zones/{id:guid}", async (Guid id, ZoneInput input, DeliveriesService svc, CancellationToken ct) => await svc.UpdateZoneAsync(id, input, ct)).WithName("admin.zones.update");
    }
}
