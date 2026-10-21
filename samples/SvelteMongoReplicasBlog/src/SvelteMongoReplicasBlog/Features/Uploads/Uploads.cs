using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Infrastructure;

namespace SvelteMongoReplicasBlog.Features;

public static class Uploads
{
    public static void MapUploads(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/uploads").RequireAuthorization().RequireRateLimiting("uploads");
        group.MapPost("/presign", PresignAsync);
        group.MapPost("/{id:guid}/confirm", ConfirmAsync);
    }

    static async Task<IResult> PresignAsync(
        PresignInput input,
        HttpContext ctx,
        AppDb db,
        Storage storage,
        CancellationToken ct
    )
    {
        var result = await UploadsService.PresignAsync(
            input,
            ctx.User.UserId(),
            db,
            storage,
            ct
        );
        if (result.Errors is { } errors)
            return Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity);
        return Results.Ok(
            new
            {
                assetId = result.AssetId,
                url = result.Url,
                headers = result.Headers,
            }
        );
    }

    static async Task<IResult> ConfirmAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        Storage storage,
        CancellationToken ct
    )
    {
        var result = await UploadsService.ConfirmAsync(
            id,
            ctx.User.UserId(),
            db,
            storage,
            ct
        );
        if (result.NotFound)
            return Results.NotFound();
        if (result.Errors is { } errors)
            return Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity);
        return Results.Ok(
            new
            {
                id = result.Id,
                url = result.Url,
                contentType = result.ContentType,
                size = result.Size,
            }
        );
    }
}
