using System.Security.Claims;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Uploads
{
    public static void MapUploads(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/uploads").RequireAuthorization().RequireRateLimiting("uploads");
        group.MapPost("/presign", (PresignInput input, ClaimsPrincipal user, UploadsService svc, CancellationToken ct) =>
            svc.PresignAsync(input, user.UserId(), ct));
        group.MapPost("/{id:guid}/confirm", (Guid id, ClaimsPrincipal user, UploadsService svc, CancellationToken ct) =>
            svc.ConfirmAsync(id, user.UserId(), ct));
    }
}
