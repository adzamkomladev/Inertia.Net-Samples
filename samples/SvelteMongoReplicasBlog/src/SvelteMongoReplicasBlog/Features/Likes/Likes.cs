using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Features.Discovery;
using SvelteMongoReplicasBlog.Infrastructure;

namespace SvelteMongoReplicasBlog.Features;

public static class Likes
{
    public static void MapLikes(this IEndpointRouteBuilder app)
    {
        app.MapMethods(
                "/posts/{id:guid}/like",
                ["POST", "PUT", "DELETE"],
                SetPostLikeAsync
            )
            .RequireAuthorization("verified")
            .RequireRateLimiting("writes");
    }

    static async Task<IResult> SetPostLikeAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        DiscoveryService discovery,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var desiredState = HttpMethods.IsPut(ctx.Request.Method)
            ? true
            : HttpMethods.IsDelete(ctx.Request.Method)
                ? false
                : (bool?)null;
        return (
            await LikesService.SetPostLikeAsync(
                id,
                ctx.User.UserId(),
                desiredState,
                db,
                jobs,
                discovery,
                realtime,
                ct
            )
        ).Respond(ctx);
    }
}
