using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Infrastructure;
using static SvelteMongoReplicasBlog.Features.PostsHelpers;

namespace SvelteMongoReplicasBlog.Features;

public static partial class Posts
{
    public static void MapPosts(this IEndpointRouteBuilder app)
    {
        app.MapGet("/posts", FeedAsync);
        app.MapInertia("/posts/create", "Posts/Create").RequireAuthorization("verified");
        app.MapPost("/posts", CreateAsync)
            .RequireAuthorization("verified")
            .WithInertiaPrecognition()
            .RequireRateLimiting("writes");
        app.MapGet("/posts/{slug}", ShowAsync);
        app.MapGet("/posts/{slug}/edit", EditAsync).RequireAuthorization();
        app.MapPut("/posts/{id:guid}", UpdateAsync)
            .RequireAuthorization("verified")
            .WithInertiaPrecognition()
            .RequireRateLimiting("writes");
        app.MapPost("/posts/{id:guid}/view", PostsService.TrackViewAsync)
            .RequireRateLimiting("views");
        app.MapDelete("/posts/{id:guid}", DeleteAsync)
            .RequireAuthorization()
            .RequireRateLimiting("writes");
    }

    internal static async Task<IResult> FeedAsync(
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
            return Results.BadRequest("Invalid search engine.");
        if (!SocialHelpers.TryCursor(cursor, out var after))
            return Results.BadRequest("Invalid cursor.");
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
        return Render(
            "Posts/Index",
            new InertiaProps
            {
                ["posts"] = hasSearch
                    ? Scroll(new FeedPage(data, pageNo, hasMore), "data")
                    : Scroll(
                        new CursorPage<PostSummary>(
                            data,
                            cursor,
                            hasMore && data.Count > 0
                                ? SocialHelpers.Cursor(data[^1].CreatedAt, data[^1].Id)
                                : null
                        ),
                        "data"
                    ),
                ["filters"] = new { q = q ?? "" },
                ["searchEngine"] = Always(external ? "meilisearch" : "native"),
            }
        );
    }

    internal static async Task<IResult> CreateAsync(
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
            return CoverError();
        var normalizedTopics = SocialHelpers.NormalizeTopics(input.Topics);
        if (
            normalizedTopics.Length > 5
            || normalizedTopics.Any(t =>
                t.Length is 0 or > 32 || !Regex.IsMatch(t, "^[a-z0-9]+(?:-[a-z0-9]+)*$")
            )
        )
            return Back()
                .WithErrors(
                    new Dictionary<string, string>
                    {
                        ["topics"] =
                            "Use up to five topics of 1–32 lowercase letters, numbers or hyphens.",
                    }
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
        ctx.Inertia().Flash("toast", new Toast("success", "Post published."));
        return Results.Redirect($"/posts/{slug}");
    }

    internal static async Task<IResult> ShowAsync(
        string slug,
        HttpContext ctx,
        AppDb db,
        Storage storage,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserIdOrNull();
        var d = await PostsService.LoadDetail(db, storage, slug, uid, ct);
        if (d is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        return Render(
            "Posts/Show",
            new InertiaProps
            {
                ["post"] = d,
                ["comments"] = Defer(
                    async (CancellationToken c) =>
                        (
                            await CommentsService.LoadPageAsync(
                                db,
                                storage,
                                d.Id,
                                d.Author.Id,
                                uid,
                                null,
                                c
                            )
                        ).Data,
                    group: "comments"
                ),
            }
        );
    }

    internal static async Task<IResult> EditAsync(
        string slug,
        HttpContext ctx,
        AppDb db,
        Storage storage,
        CancellationToken ct
    )
    {
        var d = await PostsService.LoadDetail(db, storage, slug, ctx.User.UserIdOrNull(), ct);
        if (d is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        return d.CanEdit
            ? Render("Posts/Edit", new InertiaProps { ["post"] = d })
            : HttpExtensions.Forbidden();
    }

    internal static async Task<IResult> UpdateAsync(
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
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (post.AuthorId != uid)
            return HttpExtensions.Forbidden();
        if (await PostsService.CoverInvalid(db, input.CoverAssetId, uid, ct))
            return CoverError();
        var normalizedTopics = SocialHelpers.NormalizeTopics(input.Topics);
        if (
            normalizedTopics.Length > 5
            || normalizedTopics.Any(t =>
                t.Length is 0 or > 32 || !Regex.IsMatch(t, "^[a-z0-9]+(?:-[a-z0-9]+)*$")
            )
        )
            return Back()
                .WithErrors(
                    new Dictionary<string, string>
                    {
                        ["topics"] =
                            "Use up to five topics of 1–32 lowercase letters, numbers or hyphens.",
                    }
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
                return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
            if (current.AuthorId != uid)
                return HttpExtensions.Forbidden();
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        }

        if (useSearch || coverDeleteQueued)
            jobs.Wake();
        await realtime.ChangedAsync(id);
        ctx.Inertia().Flash("toast", new Toast("success", "Post updated."));
        return Results.Redirect($"/posts/{updatedPost.Slug}");
    }

    internal static async Task<IResult> DeleteAsync(
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
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (post.AuthorId != ctx.User.UserId())
            return HttpExtensions.Forbidden();
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
                return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
            if (current.AuthorId != ctx.User.UserId())
                return HttpExtensions.Forbidden();
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        }

        if (deleteCover || useSearch)
            jobs.Wake();
        await realtime.RemovedAsync(id, owner, wasPublic);
        ctx.Inertia().Flash("toast", new Toast("success", "Post deleted."));
        return Results.Redirect("/posts");
    }
}
