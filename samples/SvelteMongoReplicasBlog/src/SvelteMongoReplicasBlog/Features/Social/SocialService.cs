using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Infrastructure;
using static SvelteMongoReplicasBlog.Features.SocialHelpers;

namespace SvelteMongoReplicasBlog.Features;

public static partial class SocialService
{
    internal // Unique-edge upserts can race on the first insert. Retry a fresh transaction so the
    // winner's committed state is visible; WithTransaction itself handles transient conflicts.
    static async Task<T> SocialTransaction<T>(
        AppDb db,
        Func<IClientSessionHandle, CancellationToken, Task<T>> action,
        CancellationToken ct
    )
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await db.TransactionAsync(session => action(session, ct), ct);
            }
            catch (MongoWriteException ex)
                when (attempt < 3 && ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            { }
            catch (MongoCommandException ex) when (attempt < 3 && ex.Code == 11000) { }
        }
    }

    internal static async Task<List<(Post Post, DateTime At)>> PublicRepostsAsync(
        AppDb db,
        Guid userId,
        Edge? after,
        int take,
        CancellationToken ct
    )
    {
        var rows = new List<(Post Post, DateTime At)>();
        var scanAfter = after;
        while (rows.Count < take)
        {
            var filter =
                Builders<Repost>.Filter.Eq(x => x.UserId, userId)
                & After<Repost>(x => x.CreatedAt, x => x.PostId, scanAfter);
            var reposts = await db
                .Reposts.Find(filter)
                .SortByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.PostId)
                .Limit(take)
                .ToListAsync(ct);
            if (reposts.Count == 0)
                break;
            var ids = reposts.Select(x => x.PostId).ToArray();
            var posts = await db
                .Posts.Find(x => ids.Contains(x.Id) && x.ArchivedAt == null && x.AuthorId != userId)
                .ToListAsync(ct);
            var byId = posts.ToDictionary(x => x.Id);
            rows.AddRange(
                reposts
                    .Where(x => byId.ContainsKey(x.PostId))
                    .Select(x => (byId[x.PostId], x.CreatedAt))
            );
            var last = reposts[^1];
            scanAfter = new Edge(last.CreatedAt, last.PostId);
            if (reposts.Count < take)
                break;
        }

        return rows;
    }

    internal static async Task<List<(Post Post, DateTime At)>> PublicBookmarksAsync(
        AppDb db,
        Guid userId,
        Edge? after,
        int take,
        CancellationToken ct
    )
    {
        var rows = new List<(Post Post, DateTime At)>();
        var scanAfter = after;
        while (rows.Count < take)
        {
            var filter =
                Builders<Bookmark>.Filter.Eq(x => x.UserId, userId)
                & After<Bookmark>(x => x.CreatedAt, x => x.PostId, scanAfter);
            var bookmarks = await db
                .Bookmarks.Find(filter)
                .SortByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.PostId)
                .Limit(take)
                .ToListAsync(ct);
            if (bookmarks.Count == 0)
                break;
            var ids = bookmarks.Select(x => x.PostId).ToArray();
            var posts = await db
                .Posts.Find(x => ids.Contains(x.Id) && x.ArchivedAt == null)
                .ToListAsync(ct);
            var byId = posts.ToDictionary(x => x.Id);
            rows.AddRange(
                bookmarks
                    .Where(x => byId.ContainsKey(x.PostId))
                    .Select(x => (byId[x.PostId], x.CreatedAt))
            );
            var last = bookmarks[^1];
            scanAfter = new Edge(last.CreatedAt, last.PostId);
            if (bookmarks.Count < take)
                break;
        }

        return rows;
    }

    internal static async Task<List<PostSummary>> Summaries(
        AppDb db,
        Storage s,
        List<Post> posts,
        Guid? uid,
        CancellationToken ct
    )
    {
        if (posts.Count == 0)
            return [];
        var ids = posts.Select(x => x.Id).ToArray();
        var users = await db
            .Users.Find(x => posts.Select(p => p.AuthorId).Contains(x.Id))
            .ToListAsync(ct);
        var um = users.ToDictionary(x => x.Id);
        var assetIds = users
            .Where(x => x.AvatarAssetId != null)
            .Select(x => x.AvatarAssetId!.Value)
            .Concat(posts.Where(x => x.CoverAssetId != null).Select(x => x.CoverAssetId!.Value))
            .Distinct()
            .ToArray();
        var assets =
            assetIds.Length == 0
                ? []
                : await db.Assets.Find(x => assetIds.Contains(x.Id)).ToListAsync(ct);
        var am = assets.ToDictionary(x => x.Id);
        var topics = await db.PostTopics.Find(x => ids.Contains(x.PostId)).ToListAsync(ct);
        var liked = uid is { } liker
            ? await db
                .Likes.Find(x => x.UserId == liker && ids.Contains(x.PostId))
                .Project(x => x.PostId)
                .ToListAsync(ct)
            : [];
        var bookmarks = uid is { } u
            ? await db
                .Bookmarks.Find(x => x.UserId == u && ids.Contains(x.PostId))
                .Project(x => x.PostId)
                .ToListAsync(ct)
            : [];
        var reposts = uid is { } r
            ? await db
                .Reposts.Find(x => x.UserId == r && ids.Contains(x.PostId))
                .Project(x => x.PostId)
                .ToListAsync(ct)
            : [];
        var repostCounts = await db
            .Reposts.Aggregate()
            .Match(x => ids.Contains(x.PostId))
            .Group(x => x.PostId, group => new { PostId = group.Key, Count = group.Count() })
            .ToListAsync(ct);
        var repostCountByPost = repostCounts.ToDictionary(x => x.PostId, x => x.Count);
        var viewCounts = await db
            .PostViews.Aggregate()
            .Match(x => ids.Contains(x.PostId))
            .Group(x => x.PostId, group => new { PostId = group.Key, Count = group.Count() })
            .ToListAsync(ct);
        var viewCountByPost = viewCounts.ToDictionary(x => x.PostId, x => x.Count);
        return posts
            .Select(p =>
            {
                um.TryGetValue(p.AuthorId, out var author);
                var avatar =
                    author?.AvatarAssetId is { } av && am.TryGetValue(av, out var asset)
                        ? s.Url(asset.Key)
                        : null;
                var cover =
                    p.CoverAssetId is { } cid && am.TryGetValue(cid, out var coverAsset)
                        ? s.Url(coverAsset.Key)
                        : null;
                return new PostSummary(
                    p.Id,
                    p.Slug,
                    p.Title,
                    p.Excerpt,
                    null,
                    cover,
                    new AuthorDto(p.AuthorId, author?.Name ?? "Unknown", avatar),
                    p.LikeCount,
                    p.CommentCount,
                    p.CreatedAt
                )
                {
                    Topics = topics.Where(x => x.PostId == p.Id).Select(x => x.Topic).ToArray(),
                    Liked = liked.Contains(p.Id),
                    Bookmarked = bookmarks.Contains(p.Id),
                    Reposted = reposts.Contains(p.Id),
                    RepostCount = repostCountByPost.GetValueOrDefault(p.Id),
                    ViewCount = viewCountByPost.GetValueOrDefault(p.Id),
                    ArchivedAt = p.ArchivedAt,
                    CanArchive = uid == p.AuthorId,
                };
            })
            .ToList();
    }

    internal static async Task<List<UserSummary>> Users(
        AppDb db,
        Storage s,
        Guid[] ids,
        Guid? uid,
        CancellationToken ct
    )
    {
        if (ids.Length == 0)
            return [];
        var rows = await db.Users.Find(x => ids.Contains(x.Id)).ToListAsync(ct);
        var assets = rows.Where(x => x.AvatarAssetId != null)
            .Select(x => x.AvatarAssetId!.Value)
            .Distinct()
            .ToArray();
        var am =
            assets.Length == 0
                ? new Dictionary<Guid, Asset>()
                : (await db.Assets.Find(x => assets.Contains(x.Id)).ToListAsync(ct)).ToDictionary(
                    x => x.Id
                );
        var followers = await db
            .Follows.Aggregate()
            .Match(x => ids.Contains(x.FolloweeId))
            .Group(x => x.FolloweeId, group => new { UserId = group.Key, Count = group.Count() })
            .ToListAsync(ct);
        var following = await db
            .Follows.Aggregate()
            .Match(x => ids.Contains(x.FollowerId))
            .Group(x => x.FollowerId, group => new { UserId = group.Key, Count = group.Count() })
            .ToListAsync(ct);
        var followedIds = uid is { } viewerId
            ? await db
                .Follows.Find(x => x.FollowerId == viewerId && ids.Contains(x.FolloweeId))
                .Project(x => x.FolloweeId)
                .ToListAsync(ct)
            : [];
        var followerCounts = followers.ToDictionary(x => x.UserId, x => x.Count);
        var followingCounts = following.ToDictionary(x => x.UserId, x => x.Count);
        var followedSet = followedIds.ToHashSet();
        return rows.Select(x => new UserSummary(
                x.Id,
                x.Name,
                x.AvatarAssetId is { } a && am.TryGetValue(a, out var asset)
                    ? s.Url(asset.Key)
                    : null,
                uid is { } && followedSet.Contains(x.Id),
                followerCounts.GetValueOrDefault(x.Id),
                followingCounts.GetValueOrDefault(x.Id),
                uid == x.Id
            ))
            .ToList();
    }

    internal static async Task<int> PageSize(AppDb db, Guid id, CancellationToken ct) =>
        Math.Clamp(
            await db
                .Users.Find(x => x.Id == id)
                .Project(x => x.Preferences.PostsPerPage)
                .FirstOrDefaultAsync(ct),
            5,
            50
        );
}
