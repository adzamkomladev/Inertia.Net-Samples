using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Features.Discovery;
using SvelteMongoReplicasBlog.Infrastructure;

namespace SvelteMongoReplicasBlog.Features;

public static partial class CommentsService
{
    internal static async Task<IResult> CommentsPageAsync(
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
            return Results.NotFound();
        if (!SocialHelpers.TryCursor(cursor, out var after))
            return Results.BadRequest("Invalid comment cursor.");
        var page = await LoadPageAsync(db, storage, id, post.AuthorId, uid, after, ct);
        return Results.Json(new { data = page.Data, nextCursor = page.NextCursor });
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
