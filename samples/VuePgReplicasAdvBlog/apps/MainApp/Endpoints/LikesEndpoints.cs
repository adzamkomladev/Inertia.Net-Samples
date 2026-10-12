using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Discovery;
using VuePgReplicasAdvBlog.Features.Likes;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.MainApp.Endpoints;

public static class LikesEndpoints
{
    public static void MapLikes(this IEndpointRouteBuilder app) =>
            app.MapMethods("/posts/{id:guid}/like", ["POST", "PUT", "DELETE"], SetAsync).RequireAuthorization("verified").RequireRateLimiting("writes");

    static async Task<IResult> SetAsync(Guid id, HttpContext ctx, AppDb db, JobQueue jobs, DiscoveryService discovery, RealtimeEvents realtime, CancellationToken ct)
    {
        bool? desiredState = HttpMethods.IsPost(ctx.Request.Method) ? null : HttpMethods.IsPut(ctx.Request.Method);
        await LikesService.SetPostLikeAsync(id, ctx.User.UserId(), desiredState, db, jobs, discovery, realtime, ct);
        return Back();
    }
}
