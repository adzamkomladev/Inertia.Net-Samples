using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Deliveries
{
    public static void MapDeliveries(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireAuthorization("admin");

        var board = admin.MapGroup("/deliveries");
        board.MapGet("/", async (DeliveriesService svc, CancellationToken ct) =>
        {
            var data = await svc.BoardAsync(ct);
            return Render("admin/Deliveries", new InertiaProps { ["deliveries"] = data.Deliveries, ["riders"] = data.Riders, ["zones"] = data.Zones });
        });
        board.MapPost("/{id:guid}/assign", (Guid id, AssignInput input, DeliveriesService svc, CancellationToken ct) => svc.AssignRiderAsync(id, input, ct));
        board.MapPost("/{id:guid}/pickup", (Guid id, DeliveriesService svc, CancellationToken ct) => svc.PickupAsync(id, ct));
        board.MapPost("/{id:guid}/deliver", (Guid id, DeliverInput input, DeliveriesService svc, CancellationToken ct) => svc.DeliverAsync(id, input, ct));
        board.MapPost("/{id:guid}/fail", (Guid id, FailInput input, DeliveriesService svc, CancellationToken ct) => svc.FailAsync(id, input, ct));

        admin.MapPost("/riders", (RiderInput input, DeliveriesService svc, CancellationToken ct) => svc.CreateRiderAsync(input, ct));
        admin.MapPost("/riders/{id:guid}", (Guid id, RiderInput input, DeliveriesService svc, CancellationToken ct) => svc.UpdateRiderAsync(id, input, ct));
        admin.MapPost("/zones", (ZoneInput input, DeliveriesService svc, CancellationToken ct) => svc.CreateZoneAsync(input, ct));
        admin.MapPost("/zones/{id:guid}", (Guid id, ZoneInput input, DeliveriesService svc, CancellationToken ct) => svc.UpdateZoneAsync(id, input, ct));
    }
}
