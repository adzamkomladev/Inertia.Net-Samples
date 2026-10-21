using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Features.Discovery;
using SvelteMongoReplicasBlog.Infrastructure;

namespace SvelteMongoReplicasBlog.Features;

public static class Comments
{
    public static void MapComments(this IEndpointRouteBuilder app)
    {
        app.MapGet("/posts/{id:guid}/comments", CommentsPageAsync);
        app.MapPost("/posts/{id:guid}/comments", AddCommentAsync)
            .RequireAuthorization("verified")
            .WithInertiaValidation()
            .RequireRateLimiting("writes");
        app.MapDelete("/comments/{id:guid}", DeleteCommentAsync)
            .RequireAuthorization()
            .RequireRateLimiting("writes");
    }

    static async Task<IResult> CommentsPageAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        Storage storage,
        string? cursor,
        CancellationToken ct
    )
    {
        var page = await CommentsService.CommentsPageAsync(
            id,
            ctx.User.UserIdOrNull(),
            db,
            storage,
            cursor,
            ct
        );
        if (page.BadCursor)
            return Results.BadRequest("Invalid comment cursor.");
        if (page.NotFound)
            return Results.NotFound();
        return Results.Json(new { data = page.Data, nextCursor = page.NextCursor });
    }

    static async Task<IResult> AddCommentAsync(
        Guid id,
        CommentInput input,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        DiscoveryService discovery,
        RealtimeEvents realtime,
        CancellationToken ct
    ) =>
        (
            await CommentsService.AddCommentAsync(
                id,
                input,
                ctx.User.UserId(),
                db,
                jobs,
                discovery,
                realtime,
                ct
            )
        ).Respond(ctx);

    static async Task<IResult> DeleteCommentAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        DiscoveryService discovery,
        RealtimeEvents realtime,
        CancellationToken ct
    ) =>
        (
            await CommentsService.DeleteCommentAsync(
                id,
                ctx.User.UserId(),
                db,
                discovery,
                realtime,
                ct
            )
        ).Respond(ctx);
}
