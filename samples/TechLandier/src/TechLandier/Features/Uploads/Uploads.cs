using System.Security.Claims;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Uploads
{
    public static void MapUploads(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/uploads").RequireAuthorization().RequireRateLimiting("uploads");
        group.MapPost("/presign", async (PresignInput input, UploadsService svc, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var result = await svc.PresignAsync(input, user.UserId(), ct);
            return result.Problems is { } problems
                ? Results.ValidationProblem(problems, statusCode: StatusCodes.Status422UnprocessableEntity)
                : Results.Ok(new
                {
                    assetId = result.AssetId,
                    url = result.Url,
                    headers = new Dictionary<string, string> { ["Content-Type"] = result.ContentType! },
                });
        });
        group.MapPost("/{id:guid}/confirm", async (Guid id, UploadsService svc, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var result = await svc.ConfirmAsync(id, user.UserId(), ct);
            if (result.NotFound) return Results.NotFound();
            if (result.Problems is { } problems)
                return Results.ValidationProblem(problems, statusCode: StatusCodes.Status422UnprocessableEntity);
            return Results.Ok(new { id = result.Id, url = result.Url, contentType = result.ContentType, size = result.Size });
        });
    }
}
