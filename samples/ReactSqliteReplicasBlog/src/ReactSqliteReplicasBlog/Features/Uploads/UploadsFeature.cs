using ReactSqliteReplicasBlog.Data;
using ReactSqliteReplicasBlog.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using static ReactSqliteReplicasBlog.Features.Uploads.UploadsHelpers;

namespace ReactSqliteReplicasBlog.Features.Uploads;

public static class UploadsFeature
{
    public static IServiceCollection AddUploadsFeature(this IServiceCollection services) =>
        services;

    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        // The global middleware validates the antiforgery header token; the endpoint-level form check would
        // validate the same token a second time, so it is disabled here.
        app.MapPost("/uploads", UploadAsync)
            .RequireAuthorization(AuthPolicies.Verified)
            .RequireRateLimiting(RateLimits.Writes)
            .DisableAntiforgery()
            // Enforced by Kestrel before the multipart body is read and buffered for IFormFile binding.
            .WithMetadata(new RequestSizeLimitAttribute(MaxBytes + 64 * 1024));
        return app;
    }

    static async Task<IResult> UploadAsync(
        HttpContext ctx,
        IFormFile? file,
        AppDb db,
        Storage storage,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var result = await UploadsService.UploadAsync(
            ctx.User.UserId()!.Value,
            file?.OpenReadStream(),
            file?.Length ?? 0,
            db,
            storage,
            clock,
            ct
        );
        return result.Error is { } error
            ? Results.BadRequest(new { error })
            : Results.Ok(new { id = result.Id, url = result.Url });
    }
}
