using Inkwell.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Inkwell.Features.Uploads;

public static class UploadsFeature
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        // The global middleware already validates the antiforgery header, so the endpoint-level form check is off.
        app.MapPost("/uploads", async (IFormFile? file, UploadsService uploads, HttpContext ctx, CancellationToken ct) =>
            {
                var result = await uploads.UploadAsync(ctx.User.UserId()!.Value, file?.OpenReadStream(), file?.Length ?? 0, ct);
                return result.Error is { } error
                    ? Results.BadRequest(new { error })
                    : Results.Ok(new { id = result.Id, url = result.Url });
            })
            .RequireAuthorization(AuthPolicies.Verified)
            .RequireRateLimiting(RateLimits.Writes)
            .DisableAntiforgery()
            // Enforced by Kestrel before the multipart body is read and buffered for IFormFile binding.
            .WithMetadata(new RequestSizeLimitAttribute(UploadsService.MaxBytes + 64 * 1024));
        return app;
    }
}
