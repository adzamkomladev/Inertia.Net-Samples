using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Features.Discovery;
using SvelteMongoReplicasBlog.Infrastructure;

namespace SvelteMongoReplicasBlog.Features;

public static partial class PostsService
{
    internal static async Task<PostSummary> ToSummary(
        AppDb db,
        Post p,
        Storage storage,
        Guid? uid,
        CancellationToken ct
    )
    {
        var author = await db.Users.Find(x => x.Id == p.AuthorId).FirstOrDefaultAsync(ct);
        var cover = p.CoverAssetId is { } coverId
            ? await db.Assets.Find(x => x.Id == coverId).FirstOrDefaultAsync(ct)
            : null;
        var avatar = author?.AvatarAssetId is { } avatarId
            ? await db.Assets.Find(x => x.Id == avatarId).FirstOrDefaultAsync(ct)
            : null;
        var topics = await db
            .PostTopics.Find(x => x.PostId == p.Id)
            .Project(x => x.Topic)
            .ToListAsync(ct);
        var liked =
            uid is { } liker
            && await db.Likes.Find(x => x.UserId == liker && x.PostId == p.Id).AnyAsync(ct);
        var bookmarked =
            uid is { } viewer
            && await db.Bookmarks.Find(x => x.UserId == viewer && x.PostId == p.Id).AnyAsync(ct);
        var reposted =
            uid is { } reposter
            && await db.Reposts.Find(x => x.UserId == reposter && x.PostId == p.Id).AnyAsync(ct);
        var repostCount = await db.Reposts.CountDocumentsAsync(
            Builders<Repost>.Filter.Eq(x => x.PostId, p.Id),
            cancellationToken: ct
        );
        var viewCount = await db.PostViews.CountDocumentsAsync(
            Builders<PostView>.Filter.Eq(x => x.PostId, p.Id),
            cancellationToken: ct
        );
        return new PostSummary(
            p.Id,
            p.Slug,
            p.Title,
            p.Excerpt,
            null,
            cover is null ? null : storage.Url(cover.Key),
            new AuthorDto(
                author?.Id ?? p.AuthorId,
                author?.Name ?? "Unknown",
                avatar is null ? null : storage.Url(avatar.Key)
            ),
            p.LikeCount,
            p.CommentCount,
            p.CreatedAt
        )
        {
            Topics = topics.ToArray(),
            Liked = liked,
            Bookmarked = bookmarked,
            Reposted = reposted,
            RepostCount = checked((int)repostCount),
            ViewCount = checked((int)viewCount),
            ArchivedAt = p.ArchivedAt,
            CanArchive = uid == p.AuthorId,
        };
    }

    internal static async Task<PostDetail?> LoadDetail(
        AppDb db,
        Storage storage,
        string slug,
        Guid? uid,
        CancellationToken ct
    )
    {
        var p = await db
            .Posts.Find(x => x.Slug == slug && (x.ArchivedAt == null || x.AuthorId == uid))
            .FirstOrDefaultAsync(ct);
        if (p is null)
            return null;
        var s = await ToSummary(db, p, storage, uid, ct);
        var author = await db.Users.Find(x => x.Id == p.AuthorId).FirstOrDefaultAsync(ct);
        var topics = await db
            .PostTopics.Find(x => x.PostId == p.Id)
            .Project(x => x.Topic)
            .ToListAsync(ct);
        var bookmarked =
            uid is { } viewer
            && await db.Bookmarks.Find(x => x.UserId == viewer && x.PostId == p.Id).AnyAsync(ct);
        var reposted =
            uid is { } reposter
            && await db.Reposts.Find(x => x.UserId == reposter && x.PostId == p.Id).AnyAsync(ct);
        return new PostDetail(
            p.Id,
            p.Slug,
            p.Title,
            p.Excerpt,
            null,
            s.CoverUrl,
            s.Author,
            p.LikeCount,
            p.CommentCount,
            p.CreatedAt,
            p.Body,
            p.CoverAssetId,
            p.UpdatedAt,
            s.Liked,
            uid == p.AuthorId
        )
        {
            Topics = topics.ToArray(),
            Bookmarked = bookmarked,
            Reposted = reposted,
            RepostCount = s.RepostCount,
            ViewCount = s.ViewCount,
            ArchivedAt = p.ArchivedAt,
            CanArchive = uid == p.AuthorId,
        };
    }

    internal static async Task<IResult> TrackViewAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        ViewTracker views,
        DiscoveryService discovery,
        CancellationToken ct
    )
    {
        string? referrer = null;
        if (ctx.Request.HasJsonContentType() && ctx.Request.ContentLength is null or <= 4096)
        {
            if (
                ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is
                { IsReadOnly: false } limit
            )
                limit.MaxRequestBodySize = 4096;
            try
            {
                referrer = (await ctx.Request.ReadFromJsonAsync<ViewBeacon>(ct))?.Referrer;
            }
            catch (Exception e)
                when (e is System.Text.Json.JsonException or BadHttpRequestException) { }
        }

        var post = await db
            .Posts.Find(p => p.Id == id && p.ArchivedAt == null)
            .FirstOrDefaultAsync(ct);
        if (post is null)
            return Results.NotFound();
        views.Track(ctx, id, post.AuthorId, referrer is { Length: <= 2048 } ? referrer : null);
        if (ctx.User.UserIdOrNull() is { } viewerId)
            await discovery.MarkSeenAsync(db, viewerId, id, DateTime.UtcNow, ct);
        return Results.NoContent();
    }

    internal static async Task<bool> IsCoverShared(
        AppDb db,
        Guid assetId,
        Guid exceptPostId,
        CancellationToken ct
    ) =>
        await db.Posts.Find(p => p.CoverAssetId == assetId && p.Id != exceptPostId).AnyAsync(ct)
        || await db.Users.Find(u => u.AvatarAssetId == assetId).AnyAsync(ct);

    internal static async Task<bool> IsCoverShared(
        AppDb db,
        Guid assetId,
        Guid exceptPostId,
        IClientSessionHandle session,
        CancellationToken ct
    ) =>
        await db
            .Posts.Find(session, p => p.CoverAssetId == assetId && p.Id != exceptPostId)
            .AnyAsync(ct)
        || await db.Users.Find(session, u => u.AvatarAssetId == assetId).AnyAsync(ct);

    internal static async Task<bool> CoverInvalid(
        AppDb db,
        Guid? assetId,
        Guid userId,
        CancellationToken ct
    ) =>
        assetId is { } id
        && !await db
            .Assets.Find(a =>
                a.Id == id
                && a.OwnerId == userId
                && a.Status == AssetStatus.Ready
                && a.ContentType.StartsWith("image/")
            )
            .AnyAsync(ct);

    internal static async Task<bool> HasMissingSearchState(AppDb db, CancellationToken ct)
    {
        MongoDB.Bson.BsonDocument[] pipeline =
        [
            new("$match", new MongoDB.Bson.BsonDocument("ArchivedAt", MongoDB.Bson.BsonNull.Value)),
            new(
                "$lookup",
                new MongoDB.Bson.BsonDocument
                {
                    { "from", db.SearchIndexStates.CollectionNamespace.CollectionName },
                    { "localField", "_id" },
                    { "foreignField", "_id" },
                    { "as", "searchState" },
                }
            ),
            new(
                "$match",
                new MongoDB.Bson.BsonDocument(
                    "searchState",
                    new MongoDB.Bson.BsonDocument("$size", 0)
                )
            ),
            new("$limit", 1),
        ];
        return await db.Posts.Aggregate<MongoDB.Bson.BsonDocument>(pipeline).AnyAsync(ct);
    }
}
