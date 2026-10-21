using System.Text.RegularExpressions;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Features.Discovery;
using SvelteMongoReplicasBlog.Infrastructure;
using static SvelteMongoReplicasBlog.Features.SocialHelpers;

namespace SvelteMongoReplicasBlog.Features;

public static partial class Social
{
    public static void MapSocial(this IEndpointRouteBuilder app)
    {
        app.MapMethods(
                "/posts/{id:guid}/bookmark",
                ["PUT", "DELETE"],
                SetBookmarkAsync
            )
            .RequireAuthorization()
            .RequireRateLimiting("writes");
        app.MapMethods("/posts/{id:guid}/repost", ["PUT", "DELETE"], SetRepostAsync)
            .RequireAuthorization("verified")
            .RequireRateLimiting("writes");
        app.MapMethods("/posts/{id:guid}/archive", ["PUT", "DELETE"], SetArchiveAsync)
            .RequireAuthorization()
            .RequireRateLimiting("writes");
        app.MapMethods("/users/{id:guid}/follow", ["PUT", "DELETE"], SetFollowAsync)
            .RequireAuthorization()
            .RequireRateLimiting("writes");
        app.MapMethods(
                "/comments/{id:guid}/like",
                ["PUT", "DELETE"],
                SetCommentLikeAsync
            )
            .RequireAuthorization("verified")
            .RequireRateLimiting("writes");
        app.MapGet(
                "/me/posts",
                (HttpContext ctx, AppDb db, Storage s, string? cursor, CancellationToken ct) =>
                    Timeline(ctx, db, s, "mine", "My posts", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/me/archives",
                (HttpContext ctx, AppDb db, Storage s, string? cursor, CancellationToken ct) =>
                    Timeline(ctx, db, s, "archives", "Archives", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/bookmarks",
                (HttpContext ctx, AppDb db, Storage s, string? cursor, CancellationToken ct) =>
                    Timeline(ctx, db, s, "bookmarks", "Bookmarks", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet("/feed", RankedFeedAsync).RequireAuthorization();
        app.MapGet("/users", DirectoryAsync);
        app.MapGet("/users/{id:guid}", ProfileAsync);
    }

    internal static async Task<IResult> SetBookmarkAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserId();
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
                if (HttpMethods.IsPut(ctx.Request.Method))
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
            return Results.NotFound();
        return Back();
    }

    internal static async Task<IResult> SetRepostAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserId();
        var changed = await SocialService.SocialTransaction(
            db,
            async (session, token) =>
            {
                var post = await db
                    .Posts.Find(session, x => x.Id == id && x.ArchivedAt == null)
                    .FirstOrDefaultAsync(token);
                if (post is null)
                    return false;
                if (HttpMethods.IsPut(ctx.Request.Method))
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
            return Back();
        await realtime.ChangedAsync(id);
        return Back();
    }

    internal static async Task<IResult> SetArchiveAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        IConfiguration config,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserId();
        var active = HttpMethods.IsPut(ctx.Request.Method);
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
                return Results.NotFound();
            if (existing.AuthorId != uid)
                return HttpExtensions.Forbidden();
            // Repeating archive/restore is an idempotent owner operation; do not advance the
            // search version when the persisted visibility state already matches.
            return Back();
        }

        if (useSearch)
            jobs.Wake();
        if (active)
            await realtime.RemovedAsync(id, uid.ToString(), true);
        else
            await realtime.ChangedAsync(id, published: true);
        return Back();
    }

    internal static async Task<IResult> SetFollowAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserId();
        if (id == uid)
            return Results.BadRequest("You cannot follow yourself.");
        if (!await db.Users.Find(x => x.Id == id).AnyAsync(ct))
            return Results.NotFound();
        await SocialService.SocialTransaction(
            db,
            async (session, token) =>
            {
                var changed = false;
                if (HttpMethods.IsPut(ctx.Request.Method))
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
        return Back();
    }

    internal static async Task<IResult> SetCommentLikeAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserId();
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
                if (HttpMethods.IsPut(ctx.Request.Method))
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
            return Results.NotFound();
        return Back();
    }

    internal static async Task<IResult> RankedFeedAsync(
        HttpContext ctx,
        AppDb db,
        Storage s,
        DiscoveryService discovery,
        string? mode,
        string? cursor,
        CancellationToken ct
    )
    {
        if (mode is not (null or "for-you" or "following"))
            return Results.BadRequest("Invalid feed mode.");
        try
        {
            var uid = ctx.User.UserId();
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
            return Render(
                "Feed/Index",
                new InertiaProps
                {
                    ["posts"] = Scroll(
                        new CursorPage<PostSummary>(items, cursor, page.NextCursor),
                        "data"
                    ),
                    ["mode"] = mode ?? "for-you",
                    ["title"] = mode == "following" ? "Following interests" : "For you",
                    ["filters"] = new { q = "" },
                }
            );
        }
        catch (ArgumentException)
        {
            return Results.BadRequest("Invalid feed cursor.");
        }
        catch (KeyNotFoundException)
        {
            return Results.StatusCode(410);
        }
    }

    internal static async Task<IResult> DirectoryAsync(
        HttpContext ctx,
        AppDb db,
        Storage s,
        string? q,
        string? cursor,
        CancellationToken ct
    )
    {
        if (!TryCursor(cursor, out var after))
            return Results.BadRequest("Invalid cursor.");
        var filter = string.IsNullOrWhiteSpace(q)
            ? Builders<User>.Filter.Empty
            : Builders<User>.Filter.Regex(
                x => x.Name,
                new MongoDB.Bson.BsonRegularExpression(Regex.Escape(q.Trim()), "i")
            );
        if (after is { } edge)
            filter &= Builders<User>.Filter.Gt(x => x.Id, edge.Id);
        var users = await db.Users.Find(filter).SortBy(x => x.Id).Limit(21).ToListAsync(ct);
        var summaries = await SocialService.Users(
            db,
            s,
            users.Take(20).Select(x => x.Id).ToArray(),
            ctx.User.UserIdOrNull(),
            ct
        );
        return Render(
            "Users/Index",
            new InertiaProps
            {
                ["users"] = Scroll(
                    new CursorPage<UserSummary>(
                        summaries,
                        cursor,
                        users.Count > 20 ? Cursor(DateTime.UnixEpoch, users[19].Id) : null
                    ),
                    "data"
                ),
                ["filters"] = new { q = q ?? "" },
            }
        );
    }

    internal static async Task<IResult> ProfileAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        Storage s,
        string? cursor,
        CancellationToken ct
    )
    {
        var user = (await SocialService.Users(db, s, [id], ctx.User.UserIdOrNull(), ct)).FirstOrDefault();
        if (user is null)
            return Results.NotFound();
        return await Timeline(ctx, db, s, "user", user.Name, cursor, user, ct);
    }

    internal static async Task<IResult> Timeline(
        HttpContext ctx,
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
            return Results.BadRequest("Invalid cursor.");
        var uid = ctx.User.UserIdOrNull();
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
        if (profile is not null)
            return Render(
                "Users/Show",
                new InertiaProps { ["user"] = profile, ["posts"] = Scroll(page, "data") }
            );
        return Render(
            "Feed/Index",
            new InertiaProps
            {
                ["posts"] = Scroll(page, "data"),
                ["mode"] = mode,
                ["title"] = title,
                ["filters"] = new { q = "" },
            }
        );
    }
}
