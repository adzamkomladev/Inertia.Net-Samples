using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Comments;
using VuePgReplicasAdvBlog.Features.Discovery;
using VuePgReplicasAdvBlog.Features.Uploads;
using VuePgReplicasAdvBlog.Infrastructure;
using VuePgReplicasAdvBlog.MainApp.Infrastructure;

namespace VuePgReplicasAdvBlog.MainApp.Endpoints;

public static class CommentsEndpoints
{
    public static void MapComments(this IEndpointRouteBuilder app)
    {
        app.MapGet("/posts/{id:guid}/comments", PageAsync).AddEndpointFilter(new FeatureErrorFilter(json: true));
        app.MapPost("/posts/{id:guid}/comments", AddAsync).RequireAuthorization("verified").WithInertiaValidation().RequireRateLimiting("writes");
        app.MapDelete("/comments/{id:guid}", DeleteAsync).RequireAuthorization().RequireRateLimiting("writes");
    }

    static async Task<IResult> PageAsync(Guid id, HttpContext ctx, AppDb db, Storage storage, string? cursor, CancellationToken ct) =>
        Results.Json(await CommentsService.CommentsPageAsync(id, ctx.User.UserIdOrNull(), db, storage, cursor, ct));

    static async Task<IResult> AddAsync(Guid id, CommentInput input, HttpContext ctx, AppDb db, JobQueue jobs, DiscoveryService discovery, RealtimeEvents realtime, CancellationToken ct)
    {
        await CommentsService.AddCommentAsync(id, input, ctx.User.UserId(), db, jobs, discovery, realtime, ct);
        return Back();
    }

    static async Task<IResult> DeleteAsync(Guid id, HttpContext ctx, AppDb db, DiscoveryService discovery, RealtimeEvents realtime, CancellationToken ct)
    {
        await CommentsService.DeleteCommentAsync(id, ctx.User.UserId(), db, discovery, realtime, ct);
        return Back();
    }
}
