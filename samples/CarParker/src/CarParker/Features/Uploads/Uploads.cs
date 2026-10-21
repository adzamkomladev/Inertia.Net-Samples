using CarParker.Data;
using CarParker.Infrastructure;

namespace CarParker.Features;

public static class Uploads
{
    public static void MapUploads(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/uploads").RequireAuthorization().RequireRateLimiting("uploads");
        group.MapPost("/presign", PresignAsync);
        group.MapPost("/{id:guid}/confirm", ConfirmAsync);
    }

    static async Task<IResult> PresignAsync(PresignInput input, HttpContext ctx, AppDb db, Storage storage, CancellationToken ct)
    {
        var result = await UploadsService.PresignAsync(input, ctx.User.UserId(), db, storage, ct);
        return result.Problems is { } problems
            ? Results.ValidationProblem(problems, statusCode: StatusCodes.Status422UnprocessableEntity)
            : Results.Ok(
                new
                {
                    assetId = result.AssetId,
                    url = result.Url,
                    headers = new Dictionary<string, string> { ["Content-Type"] = result.ContentType! },
                }
            );
    }

    static async Task<IResult> ConfirmAsync(Guid id, HttpContext ctx, AppDb db, Storage storage, CancellationToken ct)
    {
        var result = await UploadsService.ConfirmAsync(id, ctx.User.UserId(), db, storage, ct);
        if (result.NotFound)
            return Results.NotFound();
        if (result.Problems is { } problems)
            return Results.ValidationProblem(problems, statusCode: StatusCodes.Status422UnprocessableEntity);
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
