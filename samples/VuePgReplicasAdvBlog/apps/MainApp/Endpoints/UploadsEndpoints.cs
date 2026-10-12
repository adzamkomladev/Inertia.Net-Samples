using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Uploads;
using VuePgReplicasAdvBlog.Infrastructure;
using VuePgReplicasAdvBlog.MainApp.Infrastructure;

namespace VuePgReplicasAdvBlog.MainApp.Endpoints;

public static class UploadsEndpoints
{
    public static void MapUploads(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/uploads").RequireAuthorization().RequireRateLimiting("uploads").AddEndpointFilter(new FeatureErrorFilter(json: true));
        group.MapPost("/presign", PresignAsync);
        group.MapPost("/{id:guid}/confirm", ConfirmAsync);
    }

    static async Task<IResult> PresignAsync(PresignInput input, HttpContext ctx, AppDb db, Storage storage, CancellationToken ct) =>
        Results.Ok(await UploadsService.PresignAsync(input, ctx.User.UserId(), db, storage, ct));

    static async Task<IResult> ConfirmAsync(Guid id, HttpContext ctx, AppDb db, Storage storage, CancellationToken ct) =>
        Results.Ok(await UploadsService.ConfirmAsync(id, ctx.User.UserId(), db, storage, ct));
}
