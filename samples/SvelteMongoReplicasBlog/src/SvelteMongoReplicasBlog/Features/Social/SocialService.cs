using System.Text.RegularExpressions;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Features.Discovery;
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
        var authorIds = posts.Select(x => x.AuthorId).Distinct().ToArray();
        var users = await db.Users.Find(x => authorIds.Contains(x.Id)).ToListAsync(ct);
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
        var topics = (
            await db.PostTopics.Find(x => ids.Contains(x.PostId)).ToListAsync(ct)
        ).ToLookup(x => x.PostId, x => x.Topic);
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
                    Topics = topics[p.Id].ToArray(),
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
    /// <summary>Whether the bookmark command found its target post.</summary>
    public sealed record SetBookmarkResult(bool NotFound = false);
    /// <summary>Whether the archive command found its target and whether the caller owns it.</summary>
    public sealed record SetArchiveResult(bool NotFound = false, bool Forbidden = false);
    /// <summary>Whether the follow command hit a guard (self-follow or unknown user).</summary>
    public sealed record FollowResult(bool SelfFollow = false, bool UserMissing = false);
    /// <summary>Whether the comment-like command found its target.</summary>
    public sealed record CommentLikeResult(bool NotFound = false);
    /// <summary>The ranked /feed page data.</summary>
    public sealed record FeedData(
        List<PostSummary> Items,
        string? NextCursor,
        string Mode,
        string Title,
        bool BadMode = false,
        bool BadCursor = false,
        bool Gone = false
    );
    /// <summary>The /users directory page data.</summary>
    public sealed record DirectoryData(List<UserSummary> Users, string? Next, bool BadCursor = false);
    /// <summary>The timeline (mine/archives/bookmarks/user) page data.</summary>
    public sealed record TimelineData(List<PostSummary> Items, string? Next, bool BadCursor = false);


    internal static async Task<SetBookmarkResult> SetBookmarkAsync(
        Guid id,
        Guid uid,
        bool active,
        AppDb db,
        CancellationToken ct
    )
    {
        var changed = await SocialService.SocialTransaction(
            db,
            async (session, token) =>
            {
                var post = await db.Posts.UpdateOneAsync(
                    session,
                    x => x.Id == id && (x.ArchivedAt == null || x.AuthorId == uid),
                    Builders<Post>.Update.Inc(x => x.SocialRevision, 1),
                    cancellationToken: token
                );
                if (post.MatchedCount == 0)
                    return false;
                if (active)
                    await db.Bookmarks.UpdateOneAsync(
                        session,
                        x => x.UserId == uid && x.PostId == id,
                        Builders<Bookmark>
                            .Update.SetOnInsert(x => x.Id, $"{uid}:{id}")
                            .SetOnInsert(x => x.UserId, uid)
                            .SetOnInsert(x => x.PostId, id)
                            .SetOnInsert(x => x.CreatedAt, DateTime.UtcNow),
                        new UpdateOptions { IsUpsert = true },
                        token
                    );
                else
                    await db.Bookmarks.DeleteOneAsync(
                        session,
                        x => x.UserId == uid && x.PostId == id,
                        cancellationToken: token
                    );
                return true;
            },
            ct
        );
        if (!changed)
            return new SetBookmarkResult(NotFound: true);
        return new SetBookmarkResult();
    }
    internal static async Task<Outcome> SetRepostAsync(
        Guid id,
        Guid uid,
        bool active,
        AppDb db,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var changed = await SocialService.SocialTransaction(
            db,
            async (session, token) =>
            {
                var post = await db
                    .Posts.Find(session, x => x.Id == id && x.ArchivedAt == null)
                    .FirstOrDefaultAsync(token);
                if (post is null)
                    return false;
                if (active)
                {
                    var result = await db.Reposts.UpdateOneAsync(
                        session,
                        x => x.UserId == uid && x.PostId == id,
                        Builders<Repost>
                            .Update.SetOnInsert(x => x.Id, Guid.CreateVersion7())
                            .SetOnInsert(x => x.UserId, uid)
                            .SetOnInsert(x => x.PostId, id)
                            .SetOnInsert(x => x.CreatedAt, DateTime.UtcNow),
                        new UpdateOptions { IsUpsert = true },
                        token
                    );
                    if (result.UpsertedId is null)
                        return false;
                }
                else if (
                    (
                        await db.Reposts.DeleteOneAsync(
                            session,
                            x => x.UserId == uid && x.PostId == id,
                            cancellationToken: token
                        )
                    ).DeletedCount == 0
                )
                    return false;
                await db.Posts.UpdateOneAsync(
                    session,
                    x => x.Id == id,
                    Builders<Post>.Update.Inc(x => x.SocialRevision, 1),
                    cancellationToken: token
                );
                return true;
            },
            ct
        );
        if (!changed)
            return Outcome.Ok();
        await realtime.ChangedAsync(id);
        return Outcome.Ok();
    }

    internal static async Task<SetArchiveResult> SetArchiveAsync(
        Guid id,
        Guid uid,
        bool active,
        AppDb db,
        JobQueue jobs,
        IConfiguration config,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var at = active ? DateTime.UtcNow : (DateTime?)null;
        var changed = false;
        var useSearch = SearchIndexing.IsMeilisearchEnabled(config);
        await db.TransactionAsync(
            async session =>
            {
                var result = await db.Posts.UpdateOneAsync(
                    session,
                    x =>
                        x.Id == id
                        && x.AuthorId == uid
                        && (active ? x.ArchivedAt == null : x.ArchivedAt != null),
                    Builders<Post>
                        .Update.Set(x => x.ArchivedAt, at)
                        .Inc(x => x.IndexVersion, 1)
                        .Inc(x => x.SocialRevision, 1),
                    cancellationToken: ct
                );
                changed = result.ModifiedCount > 0;
                if (changed && useSearch)
                {
                    var post = await db.Posts.Find(session, x => x.Id == id).FirstAsync(ct);
                    await SearchIndexing.RequestAsync(
                        db,
                        jobs,
                        id,
                        post.IndexVersion,
                        session,
                        SearchIndexing.IsMeilisearchEnabled(config),
                        ct
                    );
                }
            },
            ct
        );
        if (!changed)
        {
            var existing = await db.Posts.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
            if (existing is null)
                return new SetArchiveResult(NotFound: true);
            if (existing.AuthorId != uid)
                return new SetArchiveResult(Forbidden: true);
            // Repeating archive/restore is an idempotent owner operation; do not advance the
            // search version when the persisted visibility state already matches.
            return new SetArchiveResult();
        }

        if (useSearch)
            jobs.Wake();
        if (active)
            await realtime.RemovedAsync(id, uid.ToString(), true);
        else
            await realtime.ChangedAsync(id, published: true);
        return new SetArchiveResult();
    }

    internal static async Task<FollowResult> SetFollowAsync(
        Guid id,
        Guid uid,
        bool active,
        AppDb db,
        CancellationToken ct
    )
    {
        if (id == uid)
            return new FollowResult(SelfFollow: true);
        if (!await db.Users.Find(x => x.Id == id).AnyAsync(ct))
            return new FollowResult(UserMissing: true);
        await SocialService.SocialTransaction(
            db,
            async (session, token) =>
            {
                var changed = false;
                if (active)
                {
                    var result = await db.Follows.UpdateOneAsync(
                        session,
                        x => x.FollowerId == uid && x.FolloweeId == id,
                        Builders<Follow>
                            .Update.SetOnInsert(x => x.Id, $"{uid}:{id}")
                            .SetOnInsert(x => x.FollowerId, uid)
                            .SetOnInsert(x => x.FolloweeId, id)
                            .SetOnInsert(x => x.CreatedAt, DateTime.UtcNow),
                        new UpdateOptions { IsUpsert = true },
                        token
                    );
                    changed = result.UpsertedId is not null;
                }
                else
                {
                    var result = await db.Follows.DeleteOneAsync(
                        session,
                        x => x.FollowerId == uid && x.FolloweeId == id,
                        cancellationToken: token
                    );
                    changed = result.DeletedCount > 0;
                }

                if (changed)
                    await db.FeedSnapshots.DeleteManyAsync(
                        session,
                        x => x.UserId == uid && x.Mode == "following",
                        cancellationToken: token
                    );
                return true;
            },
            ct
        );
        return new FollowResult();
    }

    internal static async Task<CommentLikeResult> SetCommentLikeAsync(
        Guid id,
        Guid uid,
        bool active,
        AppDb db,
        CancellationToken ct
    )
    {
        var changed = await SocialService.SocialTransaction(
            db,
            async (session, token) =>
            {
                var comment = await db
                    .Comments.Find(session, x => x.Id == id)
                    .FirstOrDefaultAsync(token);
                if (comment is null)
                    return false;
                var post = await db.Posts.UpdateOneAsync(
                    session,
                    x => x.Id == comment.PostId && x.ArchivedAt == null,
                    Builders<Post>.Update.Inc(x => x.SocialRevision, 1),
                    cancellationToken: token
                );
                if (post.MatchedCount == 0)
                    return false;
                if (active)
                    await db.CommentLikes.UpdateOneAsync(
                        session,
                        x => x.CommentId == id && x.UserId == uid,
                        Builders<CommentLike>
                            .Update.SetOnInsert(x => x.Id, $"{id}:{uid}")
                            .SetOnInsert(x => x.CommentId, id)
                            .SetOnInsert(x => x.UserId, uid)
                            .SetOnInsert(x => x.CreatedAt, DateTime.UtcNow),
                        new UpdateOptions { IsUpsert = true },
                        token
                    );
                else
                    await db.CommentLikes.DeleteOneAsync(
                        session,
                        x => x.CommentId == id && x.UserId == uid,
                        cancellationToken: token
                    );
                return true;
            },
            ct
        );
        if (!changed)
            return new CommentLikeResult(NotFound: true);
        return new CommentLikeResult();
    }

    internal static async Task<FeedData> RankedFeedAsync(
        Guid uid,
        AppDb db,
        Storage s,
        DiscoveryService discovery,
        string? mode,
        string? cursor,
        CancellationToken ct
    )
    {
        if (mode is not (null or "for-you" or "following"))
            return new FeedData([], null, mode ?? "for-you", "", BadMode: true);
        try
        {
            var pageSize = await SocialService.PageSize(db, uid, ct);
            var page = await discovery.GetPageAsync(
                db,
                uid,
                mode == "following"
                    ? DiscoveryFeedMode.FollowingInterests
                    : DiscoveryFeedMode.ForYou,
                cursor == "start" ? null : cursor,
                pageSize,
                ct
            );
            var ids = page.Items.Select(x => x.PostId).ToArray();
            var posts =
                ids.Length == 0
                    ? []
                    : await db
                        .Posts.Find(x => ids.Contains(x.Id) && x.ArchivedAt == null)
                        .ToListAsync(ct);
            var byId = (await SocialService.Summaries(db, s, posts, uid, ct)).ToDictionary(x => x.Id);
            var items = page
                .Items.Where(x => byId.ContainsKey(x.PostId))
                .Select(x => byId[x.PostId] with { Reason = x.Reason })
                .ToList();
            return new FeedData(items, page.NextCursor, mode ?? "for-you",
                mode == "following" ? "Following interests" : "For you");
        }
        catch (ArgumentException)
        {
            return new FeedData([], null, mode ?? "for-you", "", BadCursor: true);
        }
        catch (KeyNotFoundException)
        {
            return new FeedData([], null, mode ?? "for-you", "", Gone: true);
        }
    }

    internal static async Task<DirectoryData> DirectoryAsync(
        Guid? uid,
        AppDb db,
        Storage s,
        string? q,
        string? cursor,
        CancellationToken ct
    )
    {
        if (!TryCursor(cursor, out var after))
            return new DirectoryData([], null, BadCursor: true);
        var filter = string.IsNullOrWhiteSpace(q)
            ? Builders<User>.Filter.Empty
            : Builders<User>.Filter.Regex(
                x => x.Name,
                new MongoDB.Bson.BsonRegularExpression(Regex.Escape(q.Trim()), "i")
            );
        if (after is { } edge)
            filter &= Builders<User>.Filter.Gt(x => x.Id, edge.Id);
        var users = await db.Users.Find(filter).SortBy(x => x.Id).Limit(21).ToListAsync(ct);
        var summaries = await Users(
            db,
            s,
            users.Take(20).Select(x => x.Id).ToArray(),
            uid,
            ct
        );
        return new DirectoryData(summaries, users.Count > 20 ? Cursor(DateTime.UnixEpoch, users[19].Id) : null);
    }

    internal static async Task<TimelineData> Timeline(
        Guid? uid,
        AppDb db,
        Storage storage,
        string mode,
        string title,
        string? cursor,
        UserSummary? profile,
        CancellationToken ct
    )
    {
        if (!TryCursor(cursor, out var after))
            return new TimelineData([], null, BadCursor: true);
        var ownerId = profile?.Id ?? uid ?? Guid.Empty;
        var limit = uid is { } userId ? await SocialService.PageSize(db, userId, ct) : 10;
        var rows = new List<(Post Post, DateTime At)>();
        if (mode is "mine" or "user")
        {
            var authorId = mode == "mine" ? ownerId : profile!.Id;
            var ownFilter =
                Builders<Post>.Filter.Eq(x => x.AuthorId, authorId)
                & Builders<Post>.Filter.Eq(x => x.ArchivedAt, null);
            ownFilter &= SocialService.After<Post>(x => x.CreatedAt, x => x.Id, after);
            var own = await db
                .Posts.Find(ownFilter)
                .SortByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .Limit(limit + 1)
                .ToListAsync(ct);
            rows.AddRange(own.Select(x => (x, x.CreatedAt)));
            rows.AddRange(await SocialService.PublicRepostsAsync(db, authorId, after, limit + 1, ct));
        }
        else if (mode == "archives")
        {
            var filter =
                Builders<Post>.Filter.Eq(x => x.AuthorId, ownerId)
                & Builders<Post>.Filter.Ne(x => x.ArchivedAt, null);
            filter &= SocialService.After<Post>(x => x.CreatedAt, x => x.Id, after);
            var posts = await db
                .Posts.Find(filter)
                .SortByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .Limit(limit + 1)
                .ToListAsync(ct);
            rows.AddRange(posts.Select(x => (x, x.CreatedAt)));
        }
        else
        {
            rows.AddRange(await SocialService.PublicBookmarksAsync(db, ownerId, after, limit + 1, ct));
        }

        rows = rows.GroupBy(x => x.Post.Id)
            .Select(g => g.OrderByDescending(x => x.At).First())
            .OrderByDescending(x => x.At)
            .ThenByDescending(x => x.Post.Id, GuidByteOrder.Instance)
            .Take(limit + 1)
            .ToList();
        var more = rows.Count > limit;
        var chosen = rows.Take(limit).ToList();
        var items = await SocialService.Summaries(db, storage, chosen.Select(x => x.Post).ToList(), uid, ct);
        var atById = chosen.ToDictionary(x => x.Post.Id, x => x.At);
        items = items
            .Select(x => x with { CreatedAt = atById.GetValueOrDefault(x.Id, x.CreatedAt) })
            .ToList();
        if (mode is "mine" or "user")
        {
            var reposterId = mode == "mine" ? ownerId : profile!.Id;
            var reposter = (await SocialService.Users(db, storage, [reposterId], uid, ct)).FirstOrDefault();
            if (reposter is not null)
                items = items
                    .Select(x =>
                        x.Author.Id == reposterId
                            ? x
                            : x with
                            {
                                Reposter = new AuthorDto(
                                    reposter.Id,
                                    reposter.Name,
                                    reposter.AvatarUrl
                                ),
                            }
                    )
                    .ToList();
        }

        var next = more && chosen.Count > 0 ? Cursor(chosen[^1].At, chosen[^1].Post.Id) : null;
        var page = new CursorPage<PostSummary>(items, cursor, next);
        return new TimelineData(items, next);
    }

    internal static async Task<UserSummary?> ProfileAsync(
        Guid id,
        Guid? uid,
        AppDb db,
        Storage s,
        string? cursor,
        CancellationToken ct
    ) => (await Users(db, s, [id], uid, ct)).FirstOrDefault();
}
