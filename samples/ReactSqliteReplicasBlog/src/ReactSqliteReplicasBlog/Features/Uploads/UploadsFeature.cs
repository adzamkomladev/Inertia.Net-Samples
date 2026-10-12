using ReactSqliteReplicasBlog.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using static ReactSqliteReplicasBlog.Features.Uploads.UploadsHelpers;
using static ReactSqliteReplicasBlog.Features.Uploads.UploadsService;

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
}
