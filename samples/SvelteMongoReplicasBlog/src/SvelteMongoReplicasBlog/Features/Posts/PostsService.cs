using Microsoft.AspNetCore.Mvc;
using static SvelteMongoReplicasBlog.Features.PostsHelpers;
using System.Text.RegularExpressions;
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
    /// <summary>The feed page data: summaries plus everything the handler needs for the scroll props.</summary>
    public sealed record FeedResult(
        List<PostSummary> Data,
        string? Term,
        int Page,
        bool HasMore,
        string? Next,
        bool ExternalSearch,
        bool HasSearch,
        bool BadEngine,
        bool BadCursor
    );


    public static async Task<FeedResult> FeedDataAsync(
        HttpContext ctx,
        AppDb db,
        Storage storage,
        OptionalServiceNamespace names,
        [FromServices] MeilisearchTaskClient? search,
        string? q,
        int? page,
        string? engine,
        string? cursor,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserIdOrNull();
        var user = uid is { } userId
            ? await db.Users.Find(x => x.Id == userId).FirstOrDefaultAsync(ct)
            : null;
        var size = user?.Preferences.PostsPerPage is > 0 and <= 100
            ? user.Preferences.PostsPerPage
            : 10;
        if (engine is not (null or "native" or "meilisearch"))
            return new FeedResult([], null, 1, false, null, false, false, true, false);
        if (!SocialHelpers.TryCursor(cursor, out var after))
            return new FeedResult([], null, 1, false, null, false, false, BadEngine: false, BadCursor: true);
        var pageNo = Math.Max(1, page ?? 1);
        var external = false;
        List<Post>? rows = null;
        var hasSearch = !string.IsNullOrWhiteSpace(q);
        var filter = Builders<Post>.Filter.Eq(p => p.ArchivedAt, null);
        if (!hasSearch)
        {
            if (after is { } edge)
                filter &=
                    Builders<Post>.Filter.Lte(p => p.CreatedAt, edge.At)
                    & (
                        Builders<Post>.Filter.Lt(p => p.CreatedAt, edge.At)
                        | (
                            Builders<Post>.Filter.Eq(p => p.CreatedAt, edge.At)
                            & Builders<Post>.Filter.Lt(p => p.Id, edge.Id)
                        )
                    );
            rows = await db
                .Posts.Find(filter)
                .SortByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .Limit(size + 1)
                .ToListAsync(ct);
        }
        else
        {
            var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
            if (
                search is not null
                && SearchIndexing.IsMeilisearchEnabled(config)
                && !await SearchIndexing.IsReindexInProgressAsync(db, ct)
                && !await db
                    .SearchIndexStates.Find(x => x.RequestedVersion > x.AppliedVersion)
                    .AnyAsync(ct)
                && !await PostsService.HasMissingSearchState(db, ct)
            )
            {
                try
                {
                    var hits = await search.SearchAsync(
                        names.MeilisearchIndex("posts"),
                        q!.Trim(),
                        size + 1,
                        engine is not null && engine != "meilisearch" ? 0 : (pageNo - 1) * size,
                        ct
                    );
                    if (engine is not null && engine != "meilisearch")
                        pageNo = 1;
                    var ids = hits
                        .DocumentIds.Select(id =>
                            Guid.TryParse(id, out var value) ? (Guid?)value : null
                        )
                        .Where(id => id.HasValue)
                        .Select(id => id!.Value)
                        .ToArray();
                    var matched =
                        ids.Length == 0
                            ? []
                            : await db
                                .Posts.Find(x => ids.Contains(x.Id) && x.ArchivedAt == null)
                                .ToListAsync(ct);
                    var map = matched.ToDictionary(x => x.Id);
                    rows = ids.Where(map.ContainsKey).Select(id => map[id]).ToList();
                    external = true;
                }
                catch (Exception ex)
                    when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { }
            }

            if (rows is null)
            {
                if (engine is not null && engine != "native")
                    pageNo = 1;
                filter &= Builders<Post>.Filter.Text(q!.Trim());
                rows = await db
                    .Posts.Find(filter)
                    .Sort(
                        Builders<Post>
                            .Sort.MetaTextScore("score")
                            .Descending(p => p.CreatedAt)
                            .Descending(p => p.Id)
                    )
                    .Skip((pageNo - 1) * size)
                    .Limit(size + 1)
                    .ToListAsync(ct);
            }
        }

        var hasMore = rows.Count > size;
        var data = await SocialService.Summaries(db, storage, rows.Take(size).ToList(), uid, ct);
        return new FeedResult(
            data,
            q?.Trim(),
            pageNo,
            hasMore,
            hasMore && data.Count > 0 ? SocialHelpers.Cursor(data[^1].CreatedAt, data[^1].Id) : null,
            external,
            hasSearch,
            false,
            false
        );
    }

    public static async Task<Outcome> CreateAsync(
        PostInput input,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        IConfiguration config,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserId();
        if (await PostsService.CoverInvalid(db, input.CoverAssetId, uid, ct))
            return Outcome.Fail("coverAssetId", "Upload an image first.");
        var normalizedTopics = SocialHelpers.NormalizeTopics(input.Topics);
        if (
            normalizedTopics.Length > 5
            || normalizedTopics.Any(t =>
                t.Length is 0 or > 32 || !Regex.IsMatch(t, "^[a-z0-9]+(?:-[a-z0-9]+)*$")
            )
        )
            return Outcome.Fail(
                "topics",
                "Use up to five topics of 1–32 lowercase letters, numbers or hyphens."
            );
        var title = input.Title!.Trim();
        var baseSlug = Slugify(title);
        var slug = baseSlug;
        var useSearch = SearchIndexing.IsMeilisearchEnabled(config);
        if (Reserved.Contains(slug) || await db.Posts.Find(p => p.Slug == slug).AnyAsync(ct))
            slug = baseSlug + "-" + RandomSuffix();
        var publishedId = Guid.Empty;
        for (var attempt = 0; ; attempt++)
        {
            var post = new Post
            {
                AuthorId = uid,
                Title = title,
                Slug = slug,
                Body = input.Body!,
                Excerpt = MakeExcerpt(input.Body!),
                IndexVersion = useSearch ? 1 : 0,
            };
            post.CoverAssetId = input.CoverAssetId;
            try
            {
                await db.TransactionAsync(
                    async session =>
                    {
                        await db.Posts.InsertOneAsync(session, post, cancellationToken: ct);
                        if (normalizedTopics.Length > 0)
                            await db.PostTopics.InsertManyAsync(
                                session,
                                normalizedTopics.Select(topic => new PostTopic
                                {
                                    PostId = post.Id,
                                    Topic = topic,
                                }),
                                cancellationToken: ct
                            );
                        if (useSearch)
                            await SearchIndexing.RequestAsync(
                                db,
                                jobs,
                                post.Id,
                                post.IndexVersion,
                                session,
                                SearchIndexing.IsMeilisearchEnabled(config),
                                ct
                            );
                    },
                    ct
                );
                if (useSearch)
                    jobs.Wake();
                publishedId = post.Id;
                break;
            }
            catch (MongoWriteException e)
                when (attempt < 3 && e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                slug = baseSlug + "-" + RandomSuffix();
            }
        }

        await realtime.ChangedAsync(publishedId, published: true);
        return Outcome.FlashThenRedirect(new Toast("success", "Post published."), $"/posts/{slug}");
    }

    public static async Task<Outcome> UpdateAsync(
        Guid id,
        PostInput input,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        IConfiguration config,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserId();
        var post = await db.Posts.Find(p => p.Id == id).FirstOrDefaultAsync(ct);
        if (post is null)
            return Outcome.Missing();
        if (post.AuthorId != uid)
            return Outcome.Denied();
        if (await PostsService.CoverInvalid(db, input.CoverAssetId, uid, ct))
            return Outcome.Fail("coverAssetId", "Upload an image first.");
        var normalizedTopics = SocialHelpers.NormalizeTopics(input.Topics);
        if (
            normalizedTopics.Length > 5
            || normalizedTopics.Any(t =>
                t.Length is 0 or > 32 || !Regex.IsMatch(t, "^[a-z0-9]+(?:-[a-z0-9]+)*$")
            )
        )
            return Outcome.Fail(
                "topics",
                "Use up to five topics of 1–32 lowercase letters, numbers or hyphens."
            );
        var oldCoverId = input.CoverAssetId == post.CoverAssetId ? null : post.CoverAssetId;
        var oldCover = oldCoverId is { } oldId
            ? await db.Assets.Find(a => a.Id == oldId).FirstOrDefaultAsync(ct)
            : null;
        var updatedAt = DateTime.UtcNow;
        var useSearch = SearchIndexing.IsMeilisearchEnabled(config);
        var update = Builders<Post>
            .Update.Set(p => p.Title, input.Title!.Trim())
            .Set(p => p.Body, input.Body!)
            .Set(p => p.Excerpt, MakeExcerpt(input.Body!))
            .Set(p => p.CoverAssetId, input.CoverAssetId)
            .Set(p => p.UpdatedAt, updatedAt);
        if (useSearch)
            update = update.Inc(p => p.IndexVersion, 1);
        var coverDeleteQueued = false;
        var updatedPost = await db.TransactionAsync(
            async session =>
            {
                coverDeleteQueued = false;
                var saved = await db.Posts.FindOneAndUpdateAsync<Post, Post>(
                    session,
                    p => p.Id == id && p.AuthorId == uid,
                    update,
                    new FindOneAndUpdateOptions<Post, Post>
                    {
                        ReturnDocument = ReturnDocument.After,
                    },
                    ct
                );
                if (saved is null)
                    return null;
                await db.PostTopics.DeleteManyAsync(
                    session,
                    Builders<PostTopic>.Filter.Where(x => x.PostId == id),
                    cancellationToken: ct
                );
                if (normalizedTopics.Length > 0)
                    await db.PostTopics.InsertManyAsync(
                        session,
                        normalizedTopics.Select(topic => new PostTopic
                        {
                            PostId = id,
                            Topic = topic,
                        }),
                        cancellationToken: ct
                    );
                if (useSearch)
                    await SearchIndexing.RequestAsync(
                        db,
                        jobs,
                        id,
                        saved.IndexVersion,
                        session,
                        useSearch,
                        ct
                    );
                if (oldCover is not null && !await PostsService.IsCoverShared(db, oldCover.Id, id, session, ct))
                {
                    await db.Assets.DeleteOneAsync(
                        session,
                        Builders<Asset>.Filter.Where(a => a.Id == oldCover.Id),
                        cancellationToken: ct
                    );
                    await jobs.EnqueueAsync(
                        db,
                        new DeleteAssets([oldCover.Key]),
                        session: session,
                        ct: ct
                    );
                    coverDeleteQueued = true;
                }

                return saved;
            },
            ct
        );
        if (updatedPost is null)
        {
            var current = await db.Posts.Find(p => p.Id == id).FirstOrDefaultAsync(ct);
            if (current is null)
                return Outcome.Missing();
            if (current.AuthorId != uid)
                return Outcome.Denied();
            return Outcome.Missing();
        }

        if (useSearch || coverDeleteQueued)
            jobs.Wake();
        await realtime.ChangedAsync(id);
        return Outcome.FlashThenRedirect(new Toast("success", "Post updated."), $"/posts/{updatedPost.Slug}");
    }

    public static async Task<Outcome> DeleteAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        IConfiguration config,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var post = await db.Posts.Find(p => p.Id == id).FirstOrDefaultAsync(ct);
        if (post is null)
            return Outcome.Missing();
        if (post.AuthorId != ctx.User.UserId())
            return Outcome.Denied();
        var owner = post.AuthorId.ToString();
        var wasPublic = post.ArchivedAt is null;
        var cover = post.CoverAssetId is { } aid
            ? await db.Assets.Find(a => a.Id == aid).FirstOrDefaultAsync(ct)
            : null;
        var deleteCover = cover is not null && !await PostsService.IsCoverShared(db, cover.Id, post.Id, ct);
        var useSearch = SearchIndexing.IsMeilisearchEnabled(config);
        var deletedPost = await db.TransactionAsync(
            async session =>
            {
                var current = await db
                    .Posts.Find(session, p => p.Id == id && p.AuthorId == ctx.User.UserId())
                    .FirstOrDefaultAsync(ct);
                if (current is null)
                    return null;
                var removed = await db.Posts.DeleteOneAsync(
                    session,
                    Builders<Post>.Filter.Where(p => p.Id == id && p.AuthorId == ctx.User.UserId()),
                    cancellationToken: ct
                );
                if (removed.DeletedCount == 0)
                    return null;
                if (useSearch)
                    await SearchIndexing.RequestAsync(
                        db,
                        jobs,
                        id,
                        current.IndexVersion + 1,
                        session,
                        useSearch,
                        ct
                    );
                if (deleteCover && cover is not null)
                {
                    await db.Assets.DeleteOneAsync(
                        session,
                        Builders<Asset>.Filter.Where(a => a.Id == cover.Id),
                        cancellationToken: ct
                    );
                    await jobs.EnqueueAsync(
                        db,
                        new DeleteAssets([cover.Key]),
                        session: session,
                        ct: ct
                    );
                }

                var commentIds = await db
                    .Comments.Find(session, Builders<Comment>.Filter.Where(x => x.PostId == id))
                    .Project(x => x.Id)
                    .ToListAsync(ct);
                if (commentIds.Count > 0)
                    await db.CommentLikes.DeleteManyAsync(
                        session,
                        Builders<CommentLike>.Filter.Where(x => commentIds.Contains(x.CommentId)),
                        cancellationToken: ct
                    );
                await db.Comments.DeleteManyAsync(
                    session,
                    Builders<Comment>.Filter.Where(x => x.PostId == id),
                    cancellationToken: ct
                );
                await db.Likes.DeleteManyAsync(
                    session,
                    Builders<Like>.Filter.Where(x => x.PostId == id),
                    cancellationToken: ct
                );
                await db.PostViews.DeleteManyAsync(
                    session,
                    Builders<PostView>.Filter.Where(x => x.PostId == id),
                    cancellationToken: ct
                );
                await db.PostDailyStats.DeleteManyAsync(
                    session,
                    Builders<PostDailyStat>.Filter.Where(x => x.PostId == id),
                    cancellationToken: ct
                );
                await db.PostTopics.DeleteManyAsync(
                    session,
                    Builders<PostTopic>.Filter.Where(x => x.PostId == id),
                    cancellationToken: ct
                );
                await db.Bookmarks.DeleteManyAsync(
                    session,
                    Builders<Bookmark>.Filter.Where(x => x.PostId == id),
                    cancellationToken: ct
                );
                await db.Reposts.DeleteManyAsync(
                    session,
                    Builders<Repost>.Filter.Where(x => x.PostId == id),
                    cancellationToken: ct
                );
                await db.SeenPosts.DeleteManyAsync(
                    session,
                    Builders<SeenPost>.Filter.Where(x => x.PostId == id),
                    cancellationToken: ct
                );
                return current;
            },
            ct
        );
        if (deletedPost is null)
        {
            var current = await db.Posts.Find(p => p.Id == id).FirstOrDefaultAsync(ct);
            if (current is null)
                return Outcome.Missing();
            if (current.AuthorId != ctx.User.UserId())
                return Outcome.Denied();
            return Outcome.Missing();
        }

        if (deleteCover || useSearch)
            jobs.Wake();
        await realtime.RemovedAsync(id, owner, wasPublic);
        return Outcome.FlashThenRedirect(new Toast("success", "Post deleted."), "/posts");
    }
}
