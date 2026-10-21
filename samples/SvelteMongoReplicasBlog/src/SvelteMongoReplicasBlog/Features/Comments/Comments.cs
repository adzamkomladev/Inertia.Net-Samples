using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Features.Discovery;
using SvelteMongoReplicasBlog.Infrastructure;

namespace SvelteMongoReplicasBlog.Features;

public static class Comments
{
    public static void MapComments(this IEndpointRouteBuilder app)
    {
        app.MapGet("/posts/{id:guid}/comments", CommentsService.CommentsPageAsync);
        app.MapPost("/posts/{id:guid}/comments", AddCommentAsync)
            .RequireAuthorization("verified")
            .WithInertiaValidation()
            .RequireRateLimiting("writes");
        app.MapDelete("/comments/{id:guid}", DeleteCommentAsync)
            .RequireAuthorization()
            .RequireRateLimiting("writes");
    }
    internal static async Task<IResult> AddCommentAsync(
        Guid id,
        CommentInput input,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        DiscoveryService discovery,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserId();
        var post = await db
            .Posts.Find(x => x.Id == id && x.ArchivedAt == null)
            .FirstOrDefaultAsync(ct);
        if (post is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        var comment = new Comment
        {
            PostId = id,
            UserId = uid,
            Body = input.Body!.Trim(),
        };
        var created = await db.TransactionAsync(
            async session =>
            {
                var changed = await db.Posts.UpdateOneAsync(
                    session,
                    x => x.Id == id && x.ArchivedAt == null,
                    Builders<Post>.Update.Inc(x => x.CommentCount, 1).Inc(x => x.SocialRevision, 1),
                    cancellationToken: ct
                );
                if (changed.ModifiedCount == 0)
                    return false;
                await db.Comments.InsertOneAsync(session, comment, cancellationToken: ct);
                await jobs.EnqueueAsync(
                    db,
                    new NotifyComment(comment.Id),
                    session: session,
                    ct: ct
                );
                return true;
            },
            ct
        );
        if (!created)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        await discovery.MarkSeenAsync(db, uid, id, DateTime.UtcNow, ct);
        jobs.Wake();
        await realtime.ChangedAsync(id, uid.ToString(), "New comment on your post");
        return Back();
    }

    internal static async Task<IResult> DeleteCommentAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        DiscoveryService discovery,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var c = await db.Comments.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
        if (c is null)
            return Back();
        var post = await db.Posts.Find(x => x.Id == c.PostId).FirstOrDefaultAsync(ct);
        var uid = ctx.User.UserId();
        if (uid != c.UserId && uid != post?.AuthorId)
            return HttpExtensions.Forbidden();
        var removed = await db.TransactionAsync(
            async session =>
            {
                var deleted = await db.Comments.DeleteOneAsync(
                    session,
                    Builders<Comment>.Filter.Where(x => x.Id == id),
                    cancellationToken: ct
                );
                if (deleted.DeletedCount > 0)
                {
                    await db.CommentLikes.DeleteManyAsync(
                        session,
                        Builders<CommentLike>.Filter.Where(x => x.CommentId == id),
                        cancellationToken: ct
                    );
                    await db.Posts.UpdateOneAsync(
                        session,
                        x => x.Id == c.PostId && x.CommentCount > 0,
                        Builders<Post>.Update.Inc(x => x.CommentCount, -1),
                        cancellationToken: ct
                    );
                    await db.Posts.UpdateOneAsync(
                        session,
                        x => x.Id == c.PostId,
                        Builders<Post>.Update.Inc(x => x.SocialRevision, 1),
                        cancellationToken: ct
                    );
                    return true;
                }

                return false;
            },
            ct
        );
        if (removed)
            await realtime.ChangedAsync(c.PostId);
        await discovery.MarkProfileDirtyAsync(db, c.UserId, DateTime.UtcNow, ct);
        return Back();
    }
}
