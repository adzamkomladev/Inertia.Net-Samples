using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Features.Discovery;
using SvelteMongoReplicasBlog.Infrastructure;

namespace SvelteMongoReplicasBlog.Features;

public static partial class CommentsService
{
    /// <summary>The comment drawer page: data plus the flags the handler turns into status codes.</summary>
    public sealed record CommentsPageResult(
        List<CommentItem> Data,
        string? NextCursor,
        bool NotFound = false,
        bool BadCursor = false
    );

    internal static async Task<CommentsPageResult> CommentsPageAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        Storage storage,
        string? cursor,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserIdOrNull();
        var post = await db
            .Posts.Find(x => x.Id == id && (x.ArchivedAt == null || x.AuthorId == uid))
            .FirstOrDefaultAsync(ct);
        if (post is null)
            return new CommentsPageResult([], null, NotFound: true);
        if (!SocialHelpers.TryCursor(cursor, out var after))
            return new CommentsPageResult([], null, BadCursor: true);
        var page = await LoadPageAsync(db, storage, id, post.AuthorId, uid, after, ct);
        return new CommentsPageResult(page.Data, page.NextCursor);
    }


    public static async Task<Outcome> AddCommentAsync(
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
            return Outcome.Missing();
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
            return Outcome.Missing();
        await discovery.MarkSeenAsync(db, uid, id, DateTime.UtcNow, ct);
        jobs.Wake();
        await realtime.ChangedAsync(id, uid.ToString(), "New comment on your post");
        return Outcome.Ok();
    }


    public static async Task<Outcome> DeleteCommentAsync(
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
            return Outcome.Ok();
        var post = await db.Posts.Find(x => x.Id == c.PostId).FirstOrDefaultAsync(ct);
        var uid = ctx.User.UserId();
        if (uid != c.UserId && uid != post?.AuthorId)
            return Outcome.Denied();
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
        return Outcome.FlashBack(new Toast("success", "Comment deleted."));
    }

    internal static async Task<(List<CommentItem> Data, string? NextCursor)> LoadPageAsync(
        AppDb db,
        Storage storage,
        Guid postId,
        Guid authorId,
        Guid? uid,
        SocialService.Edge? after,
        CancellationToken ct
    )
    {
        var filter = Builders<Comment>.Filter.Eq(x => x.PostId, postId);
        if (after is { } edge)
            filter &= Builders<Comment>.Filter.Or(
                Builders<Comment>.Filter.Lt(x => x.CreatedAt, edge.At),
                Builders<Comment>.Filter.And(
                    Builders<Comment>.Filter.Eq(x => x.CreatedAt, edge.At),
                    Builders<Comment>.Filter.Lt(x => x.Id, edge.Id)
                )
            );
        var rows = await db
            .Comments.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Limit(51)
            .ToListAsync(ct);
        var visible = rows.Take(50).ToList();
        var ids = visible.Select(x => x.Id).ToArray();
        var userIds = visible.Select(x => x.UserId).Distinct().ToArray();
        var users =
            userIds.Length == 0
                ? []
                : await db.Users.Find(x => userIds.Contains(x.Id)).ToListAsync(ct);
        var byUser = users.ToDictionary(x => x.Id);
        var assetIds = users
            .Where(x => x.AvatarAssetId != null)
            .Select(x => x.AvatarAssetId!.Value)
            .Distinct()
            .ToArray();
        var assets =
            assetIds.Length == 0
                ? []
                : await db.Assets.Find(x => assetIds.Contains(x.Id)).ToListAsync(ct);
        var byAsset = assets.ToDictionary(x => x.Id);
        var liked =
            uid is { } viewer && ids.Length > 0
                ? await db
                    .CommentLikes.Find(x => x.UserId == viewer && ids.Contains(x.CommentId))
                    .Project(x => x.CommentId)
                    .ToListAsync(ct)
                : [];
        var counts =
            ids.Length == 0
                ? []
                : await db
                    .CommentLikes.Aggregate()
                    .Match(x => ids.Contains(x.CommentId))
                    .Group(x => x.CommentId, g => new { Id = g.Key, Count = g.Count() })
                    .ToListAsync(ct);
        var countById = counts.ToDictionary(x => x.Id, x => x.Count);
        var data = visible
            .Where(x => byUser.ContainsKey(x.UserId))
            .Select(x =>
            {
                var user = byUser[x.UserId];
                var url =
                    user.AvatarAssetId is { } aid && byAsset.TryGetValue(aid, out var asset)
                        ? storage.Url(asset.Key)
                        : null;
                return new CommentItem(
                    x.Id,
                    x.Body,
                    x.CreatedAt,
                    new AuthorDto(user.Id, user.Name, url),
                    uid == x.UserId || uid == authorId,
                    liked.Contains(x.Id),
                    countById.GetValueOrDefault(x.Id)
                );
            })
            .ToArray();
        return (data.ToList(), rows.Count > 50 && data.Length > 0 ? data[^1].Cursor : null);
    }
}
