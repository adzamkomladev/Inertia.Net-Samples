namespace SvelteMongoReplicasBlog.Features;

public static class Uploads
{
    public static void MapUploads(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/uploads").RequireAuthorization().RequireRateLimiting("uploads");
        group.MapPost("/presign", UploadsService.PresignAsync);
        group.MapPost("/{id:guid}/confirm", UploadsService.ConfirmAsync);
    }
}
