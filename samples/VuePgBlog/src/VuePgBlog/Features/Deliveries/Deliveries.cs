namespace VuePgBlog.Features;

public static class Deliveries
{
    public static void MapDeliveries(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireAuthorization("admin");
        var board = admin.MapGroup("/deliveries");
        board.MapGet("/", DeliveriesService.BoardAsync);
        board.MapPost("/{id:guid}/assign", DeliveriesService.AssignAsync);
        board.MapPost("/{id:guid}/pickup", DeliveriesService.PickupAsync);
        board.MapPost("/{id:guid}/deliver", DeliveriesService.DeliverAsync);
        board.MapPost("/{id:guid}/fail", DeliveriesService.FailAsync);
        admin.MapPost("/riders", DeliveriesService.CreateRiderAsync);
        admin.MapPost("/riders/{id:guid}", DeliveriesService.UpdateRiderAsync);
        admin.MapPost("/zones", DeliveriesService.CreateZoneAsync);
        admin.MapPost("/zones/{id:guid}", DeliveriesService.UpdateZoneAsync);
    }
}
