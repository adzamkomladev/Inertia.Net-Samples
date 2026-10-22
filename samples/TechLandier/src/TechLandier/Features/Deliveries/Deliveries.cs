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
        board.MapPost("/{id:guid}/assign", async (Guid id, AssignInput input, DeliveriesService svc, CancellationToken ct) => await svc.AssignRiderAsync(id, input, ct));
        board.MapPost("/{id:guid}/pickup", async (Guid id, DeliveriesService svc, CancellationToken ct) => await svc.PickupAsync(id, ct));
        board.MapPost("/{id:guid}/deliver", async (Guid id, DeliverInput input, DeliveriesService svc, CancellationToken ct) => await svc.DeliverAsync(id, input, ct));
        board.MapPost("/{id:guid}/fail", async (Guid id, FailInput input, DeliveriesService svc, CancellationToken ct) => await svc.FailAsync(id, input, ct));

        admin.MapPost("/riders", async (RiderInput input, DeliveriesService svc, CancellationToken ct) => await svc.CreateRiderAsync(input, ct));
        admin.MapPost("/riders/{id:guid}", async (Guid id, RiderInput input, DeliveriesService svc, CancellationToken ct) => await svc.UpdateRiderAsync(id, input, ct));
        admin.MapPost("/zones", async (ZoneInput input, DeliveriesService svc, CancellationToken ct) => await svc.CreateZoneAsync(input, ct));
        admin.MapPost("/zones/{id:guid}", async (Guid id, ZoneInput input, DeliveriesService svc, CancellationToken ct) => await svc.UpdateZoneAsync(id, input, ct));
    }
}
