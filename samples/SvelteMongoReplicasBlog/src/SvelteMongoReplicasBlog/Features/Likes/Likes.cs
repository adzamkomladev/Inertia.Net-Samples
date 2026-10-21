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

    internal static async Task<IResult> SetPostLikeAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        DiscoveryService discovery,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserId();
        var changed = await db.TransactionAsync(
            async session =>
            {
                var post = await db
                    .Posts.Find(session, x => x.Id == id && x.ArchivedAt == null)
                    .FirstOrDefaultAsync(ct);
                if (post is null)
                    return ((Guid AuthorId, bool Added, bool Active, bool Changed)?)null;
                var filter = Builders<Like>.Filter.Where(x => x.PostId == id && x.UserId == uid);
                var existing = await db.Likes.Find(session, filter).FirstOrDefaultAsync(ct);
                var active =
                    HttpMethods.IsPut(ctx.Request.Method)
                    || (HttpMethods.IsPost(ctx.Request.Method) && existing is null);
                if (active && existing is null)
                {
                    await db.Likes.InsertOneAsync(
                        session,
                        new Like { PostId = id, UserId = uid },
                        cancellationToken: ct
                    );
                    await db.Posts.UpdateOneAsync(
                        session,
                        x => x.Id == id,
                        Builders<Post>
                            .Update.Inc(x => x.LikeCount, 1)
                            .Inc(x => x.SocialRevision, 1),
                        cancellationToken: ct
                    );
                    if (post.AuthorId != uid)
                        await jobs.EnqueueAsync(
                            db,
                            new NotifyLike(id, uid),
                            session: session,
                            ct: ct
                        );
                    return (AuthorId: post.AuthorId, Added: true, Active: true, Changed: true);
                }

                if (!active && existing is not null)
                {
                    var deleted = await db.Likes.DeleteOneAsync(
                        session,
                        filter,
                        cancellationToken: ct
                    );
                    if (deleted.DeletedCount > 0)
                        await db.Posts.UpdateOneAsync(
                            session,
                            x => x.Id == id && x.LikeCount > 0,
                            Builders<Post>
                                .Update.Inc(x => x.LikeCount, -1)
                                .Inc(x => x.SocialRevision, 1),
                            cancellationToken: ct
                        );
                    return (
                        AuthorId: post.AuthorId,
                        Added: false,
                        Active: false,
                        Changed: deleted.DeletedCount > 0
                    );
                }

                return (AuthorId: post.AuthorId, Added: false, Active: active, Changed: false);
            },
            ct
        );
        if (changed is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (changed.Value.Active)
            await discovery.MarkSeenAsync(db, uid, id, DateTime.UtcNow, ct);
        else
            await discovery.MarkProfileDirtyAsync(db, uid, DateTime.UtcNow, ct);
        if (changed.Value.Added && changed.Value.AuthorId != uid)
            jobs.Wake();
        if (changed.Value.Changed)
            await realtime.ChangedAsync(
                id,
                changed.Value.Added ? uid.ToString() : null,
                changed.Value.Added ? "Someone liked your post" : null
            );
        return Back();
    }
}
