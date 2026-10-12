using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Features.Discovery;
using VuePgBlog.Infrastructure;
using static VuePgBlog.Features.PostsHelpers;

namespace VuePgBlog.Features;

public static partial class PostsService
{
    internal static IQueryable<Row> Project(AppDb db, IQueryable<Post> posts) =>
        posts.Select(p => new Row(
            p.Id,
            p.Slug,
            p.Title,
            p.Excerpt,
            p.Cover == null ? null : p.Cover.Key,
            p.AuthorId,
            p.Author.Name,
            db.Assets.Where(a => a.Id == p.Author.AvatarAssetId)
                .Select(a => a.Key)
                .FirstOrDefault(),
            p.LikeCount,
            p.CommentCount,
            p.CreatedAt
        ));

    internal static async Task<PostDetail?> LoadDetail(
        AppDb db,
        Storage storage,
        System.Linq.Expressions.Expression<Func<Post, bool>> where,
        Guid? uid,
        CancellationToken ct
    )
    {
        var row = await Project(
                db,
                db.Posts.IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(where)
                    .Where(p => p.ArchivedAt == null || p.AuthorId == uid)
            )
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return null;
        var extra = await db
            .Posts.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.Id == row.Id)
            .Select(p => new
            {
                p.Body,
                p.CoverAssetId,
                p.UpdatedAt,
            })
            .FirstAsync(ct);
        var s = (await EnrichAsync(db, [ToSummary(row, null, storage)], uid, ct))[0];
        return new PostDetail(
            s.Id,
            s.Slug,
            s.Title,
            s.Excerpt,
            null,
            s.CoverUrl,
            s.Author,
            s.LikeCount,
            s.CommentCount,
            s.CreatedAt,
            extra.Body,
            extra.CoverAssetId,
            extra.UpdatedAt,
            s.Liked,
            uid == row.AuthorId
        )
        {
            Topics = s.Topics,
            Bookmarked = s.Bookmarked,
            Reposted = s.Reposted,
            RepostCount = s.RepostCount,
            ViewCount = s.ViewCount,
            ArchivedAt = s.ArchivedAt,
            CanArchive = s.CanArchive,
        };
    }

    internal static async Task<List<PostSummary>> LoadSummariesAsync(
        AppDb db,
        Storage storage,
        IQueryable<Post> query,
        Guid? uid,
        CancellationToken ct
    )
    {
        var rows = await Project(db, query.AsNoTracking()).ToListAsync(ct);
        return await EnrichAsync(
            db,
            rows.Select(r => ToSummary(r, null, storage)).ToList(),
            uid,
            ct
        );
    }

    internal static async Task<List<PostSummary>> EnrichAsync(
        AppDb db,
        List<PostSummary> items,
        Guid? uid,
        CancellationToken ct
    )
    {
        var ids = items.Select(x => x.Id).ToArray();
        if (ids.Length == 0)
            return items;
        var topics = (
            await db.PostTopics.AsNoTracking().Where(t => ids.Contains(t.PostId)).ToListAsync(ct)
        ).ToLookup(t => t.PostId, t => t.Topic);
        var states = await db
            .Posts.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id) && (p.ArchivedAt == null || p.AuthorId == uid))
            .Select(p => new
            {
                p.Id,
                p.ArchivedAt,
                Liked = uid != null && db.Likes.Any(l => l.UserId == uid && l.PostId == p.Id),
                Bookmarked = uid != null
                    && db.Bookmarks.Any(b => b.UserId == uid && b.PostId == p.Id),
                Reposted = uid != null && db.Reposts.Any(r => r.UserId == uid && r.PostId == p.Id),
                RepostCount = db.Reposts.Count(r => r.PostId == p.Id),
                ViewCount = db.PostViews.Count(v => v.PostId == p.Id),
            })
            .ToDictionaryAsync(p => p.Id, ct);
        return items
            .Where(p => states.ContainsKey(p.Id))
            .Select(p =>
                p with
                {
                    Topics = topics[p.Id].Order().ToArray(),
                    Liked = states[p.Id].Liked,
                    Bookmarked = states[p.Id].Bookmarked,
                    Reposted = states[p.Id].Reposted,
                    RepostCount = states[p.Id].RepostCount,
                    ViewCount = states[p.Id].ViewCount,
                    ArchivedAt = states[p.Id].ArchivedAt,
                    CanArchive = uid == p.Author.Id,
                }
            )
            .ToList();
    }

    internal static async Task<IResult> FeedAsync(
        HttpContext ctx,
        AppDb db,
        Storage storage,
        string? q,
        int? page,
        string? engine,
        string? cursor,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserIdOrNull();
        var size = uid is { } u
            ? await db
                .Users.Where(x => x.Id == u)
                .Select(x => x.Preferences.PostsPerPage)
                .FirstOrDefaultAsync(ct)
            : 0;
        if (size <= 0)
            size = 10;
        if (engine is not (null or "native" or "meilisearch"))
            return Results.BadRequest("Invalid search engine.");
        var pageNo = Math.Max(page ?? 1, 1);
        var skip = (pageNo - 1) * size;
        if (!SocialHelpers.TryCursor(cursor, out var after))
            return Results.BadRequest("Invalid cursor.");
        List<Row> rows;
        Dictionary<Guid, string> snippets = [];
        var match = BuildMatch(q);
        var usedExternalSearch = false;
        if (match is null)
        {
            var query = db.Posts.AsNoTracking().Where(p => p.ArchivedAt == null);
            if (after is { } edge)
                query = query.Where(p =>
                    p.CreatedAt <= edge.At
                    && (p.CreatedAt < edge.At || (p.CreatedAt == edge.At && p.Id < edge.Id))
                );
            rows = await Project(
                    db,
                    query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
                )
                .Take(size + 1)
                .ToListAsync(ct);
        }
        else
        {
            var take = size + 1;
            List<SearchHit>? hits = null;
            var external = ctx.RequestServices.GetService<MeilisearchTaskClient>();
            if (
                external is not null
                && ctx.RequestServices.GetRequiredService<IConfiguration>()["SEARCH_DRIVER"]
                    == "meilisearch"
                && !await db.SearchIndexStates.AnyAsync(
                    state => state.RequestedVersion > state.AppliedVersion,
                    ct
                )
                && !await db.Jobs.AnyAsync(
                    job =>
                        job.Type == nameof(ReindexSearchBatch)
                        && (job.Status == JobStatus.Pending || job.Status == JobStatus.Running),
                    ct
                )
                && !await db.Posts.AnyAsync(
                    post => !db.SearchIndexStates.Any(state => state.PostId == post.Id),
                    ct
                )
            )
            {
                try
                {
                    var ns = ctx.RequestServices.GetRequiredService<OptionalServiceNamespace>();
                    var result = await external.SearchAsync(
                        ns.MeilisearchIndex("posts"),
                        q!,
                        take,
                        engine is not null && engine != "meilisearch" ? 0 : skip,
                        ct
                    );
                    if (engine is not null && engine != "meilisearch")
                    {
                        pageNo = 1;
                        skip = 0;
                    }

                    hits = result
                        .DocumentIds.Select(value =>
                            Guid.TryParse(value, out var id) ? new SearchHit { Id = id } : null
                        )
                        .Where(hit => hit is not null)
                        .Cast<SearchHit>()
                        .ToList();
                    usedExternalSearch = true;
                }
                catch (Exception ex)
                    when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { }
            }

            if (hits is null && engine is not null && engine != "native")
            {
                pageNo = 1;
                skip = 0;
            }

            hits ??= await db
                .Database.SqlQuery<SearchHit>(
                    $"""
                    SELECT "Id", ts_headline('english', "Body", to_tsquery('english', {match}), 'StartSel=<mark>, StopSel=</mark>, MaxWords=24, MinWords=8') AS "Snippet"
                    FROM "Posts" WHERE "ArchivedAt" IS NULL AND to_tsvector('english', "Title" || ' ' || "Body") @@ to_tsquery('english', {match})
                    ORDER BY ts_rank(to_tsvector('english', "Title" || ' ' || "Body"), to_tsquery('english', {match})) DESC, "CreatedAt" DESC, "Id" DESC LIMIT {take} OFFSET {skip}
                    """
                )
                .ToListAsync(ct);
            var ids = hits.Select(h => h.Id).ToList();
            var byId = (
                await Project(
                        db,
                        db.Posts.AsNoTracking()
                            .Where(p => ids.Contains(p.Id) && p.ArchivedAt == null)
                    )
                    .ToListAsync(ct)
            ).ToDictionary(r => r.Id);
            rows = ids.Where(byId.ContainsKey).Select(i => byId[i]).ToList();
            snippets = hits.ToDictionary(h => h.Id, h => EncodeSnippet(h.Snippet));
        }

        var hasMore = rows.Count > size;
        var items = rows.Take(size)
            .Select(r => ToSummary(r, snippets.GetValueOrDefault(r.Id), storage))
            .ToList();
        items = await EnrichAsync(db, items, uid, ct);
        return Render(
            "Posts/Index",
            new InertiaProps
            {
                ["posts"] = match is null
                    ? Scroll(
                        new CursorPage<PostSummary>(
                            items,
                            cursor,
                            hasMore && items.Count > 0
                                ? SocialHelpers.Cursor(items[^1].CreatedAt, items[^1].Id)
                                : null
                        ),
                        "data"
                    )
                    : Scroll(new FeedPage(items, pageNo, hasMore), "data"),
                ["filters"] = new { q = q ?? "" },
                ["searchEngine"] = Always(usedExternalSearch ? "meilisearch" : "native"),
            }
        );
    }

    internal static async Task<IResult> CreateAsync(
        PostInput input,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserId();
        if (SocialHelpers.TopicsError(input.Topics) is { } error)
            return error;
        if (await CoverInvalid(db, input.CoverAssetId, uid, ct))
            return CoverError();
        var title = input.Title!.Trim();
        var baseSlug = Slugify(title);
        var slug = baseSlug;
        if (Reserved.Contains(slug) || await db.Posts.AnyAsync(p => p.Slug == slug, ct))
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
                CoverAssetId = input.CoverAssetId,
            };
            db.Posts.Add(post);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e)
                when (attempt < 3
                    && e.InnerException is Npgsql.PostgresException { SqlState: "23505" }
                )
            {
                db.Entry(post).State = EntityState.Detached;
                slug = baseSlug + "-" + RandomSuffix();
                continue;
            }

            foreach (var topic in SocialHelpers.NormalizeTopics(input.Topics))
                db.PostTopics.Add(new PostTopic { PostId = post.Id, Topic = topic });
            await db.SaveChangesAsync(ct);
            await SearchIndexing.RequestAsync(db, jobs, post.Id, post.IndexVersion, ct);
            await tx.CommitAsync(ct);
            publishedId = post.Id;
            break;
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
        var d = await LoadDetail(db, storage, p => p.Slug == slug, uid, ct);
        if (d is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        return Render(
            "Posts/Show",
            new InertiaProps
            {
                ["post"] = d,
                ["comments"] = Defer(
                    async (CancellationToken c) =>
                    {
                        var rows = await db
                            .Comments.IgnoreQueryFilters()
                            .AsNoTracking()
                            .Where(x => x.PostId == d.Id)
                            .OrderByDescending(x => x.CreatedAt)
                            .ThenByDescending(x => x.Id)
                            .Take(50)
                            .Select(x => new
                            {
                                x.Id,
                                x.Body,
                                x.CreatedAt,
                                x.UserId,
                                Name = x.User.Name,
                                AvatarKey = db
                                    .Assets.Where(a => a.Id == x.User.AvatarAssetId)
                                    .Select(a => a.Key)
                                    .FirstOrDefault(),
                                Liked = uid != null
                                    && db.CommentLikes.Any(l =>
                                        l.CommentId == x.Id && l.UserId == uid
                                    ),
                                LikeCount = db.CommentLikes.Count(l => l.CommentId == x.Id),
                            })
                            .ToListAsync(c);
                        return rows.Select(x => new CommentItem(
                                x.Id,
                                x.Body,
                                x.CreatedAt,
                                new AuthorDto(
                                    x.UserId,
                                    x.Name,
                                    x.AvatarKey is null ? null : storage.Url(x.AvatarKey)
                                ),
                                uid is { } u && (u == x.UserId || u == d.Author.Id),
                                x.Liked,
                                x.LikeCount
                            ))
                            .ToList();
                    },
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
        var d = await LoadDetail(db, storage, p => p.Slug == slug, ctx.User.UserIdOrNull(), ct);
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
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserId();
        var post = await db
            .Posts.IgnoreQueryFilters()
            .Include(p => p.Cover)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (post.AuthorId != uid)
            return HttpExtensions.Forbidden();
        if (SocialHelpers.TopicsError(input.Topics) is { } error)
            return error;
        if (await CoverInvalid(db, input.CoverAssetId, uid, ct))
            return CoverError();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var oldCover = input.CoverAssetId == post.CoverAssetId ? null : post.Cover;
        post.Title = input.Title!.Trim();
        post.Body = input.Body!;
        post.Excerpt = MakeExcerpt(input.Body!);
        post.CoverAssetId = input.CoverAssetId;
        post.UpdatedAt = DateTime.UtcNow;
        post.IndexVersion++;
        await db.PostTopics.Where(t => t.PostId == id).ExecuteDeleteAsync(ct);
        foreach (var topic in SocialHelpers.NormalizeTopics(input.Topics))
            db.PostTopics.Add(new PostTopic { PostId = id, Topic = topic });
        if (oldCover is not null && !await IsCoverShared(db, oldCover.Id, post.Id, ct))
        {
            db.Assets.Remove(oldCover);
            await jobs.EnqueueAsync(db, new DeleteAssets([oldCover.Key]), ct: ct); // also saves the post changes
        }
        else
            await db.SaveChangesAsync(ct);
        await SearchIndexing.RequestAsync(db, jobs, id, post.IndexVersion, ct);
        await tx.CommitAsync(ct);
        await realtime.ChangedAsync(id);
        ctx.Inertia().Flash("toast", new Toast("success", "Post updated."));
        return Results.Redirect($"/posts/{post.Slug}");
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
        if (
            ctx.Request.Headers.ContainsKey("X-Inertia-Prefetch")
            || ctx.Request.Headers["Purpose"]
                .ToString()
                .Contains("prefetch", StringComparison.OrdinalIgnoreCase)
            || ctx.Request.Headers["Sec-Purpose"]
                .ToString()
                .Contains("prefetch", StringComparison.OrdinalIgnoreCase)
        )
            return Results.NoContent();
        // Optional JSON { referrer } (document.referrer of the hard load); a missing/invalid body just means "use the Referer header".
        string? referrer = null;
        if (ctx.Request.HasJsonContentType() && ctx.Request.ContentLength is null or <= 4096)
        {
            // Cap the body (Content-Length may be absent with chunked encoding); oversized or bad JSON just drops the referrer.
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

        var authorId = await db
            .Posts.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p =>
                p.Id == id && (p.ArchivedAt == null || p.AuthorId == ctx.User.UserIdOrNull())
            )
            .Select(p => (Guid?)p.AuthorId)
            .FirstOrDefaultAsync(ct);
        if (authorId is not { } a)
            return Results.NotFound();
        if (ctx.User.UserIdOrNull() is { } viewer)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await discovery.MarkSeenAsync(db, viewer, id, DateTime.UtcNow, ct);
            await tx.CommitAsync(ct);
        }

        views.Track(ctx, id, a, referrer is { Length: <= 2048 } ? referrer : null);
        return Results.NoContent();
    }

    internal static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var post = await db
            .Posts.IgnoreQueryFilters()
            .Include(p => p.Cover)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (post.AuthorId != ctx.User.UserId())
            return HttpExtensions.Forbidden();
        var owner = post.AuthorId.ToString();
        var wasPublic = post.ArchivedAt is null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Posts.Remove(post);
        if (post.Cover is { } cover && !await IsCoverShared(db, cover.Id, post.Id, ct))
        {
            db.Assets.Remove(cover);
            await jobs.EnqueueAsync(db, new DeleteAssets([cover.Key]), ct: ct);
        }
        else
            await db.SaveChangesAsync(ct);
        await SearchIndexing.RequestAsync(db, jobs, id, post.IndexVersion + 1, ct);
        await tx.CommitAsync(ct);
        await realtime.RemovedAsync(id, owner, wasPublic);
        ctx.Inertia().Flash("toast", new Toast("success", "Post deleted."));
        return Results.Redirect("/posts");
    }

    internal // Asset still referenced by another post or as someone's avatar: keep it.
    static async Task<bool> IsCoverShared(
        AppDb db,
        Guid assetId,
        Guid exceptPostId,
        CancellationToken ct
    ) =>
        await db.Posts.AnyAsync(p => p.CoverAssetId == assetId && p.Id != exceptPostId, ct)
        || await db.Users.AnyAsync(u => u.AvatarAssetId == assetId, ct);

    internal static async Task<bool> CoverInvalid(
        AppDb db,
        Guid? assetId,
        Guid userId,
        CancellationToken ct
    ) =>
        assetId is { } id
        && !await db.Assets.AnyAsync(
            a =>
                a.Id == id
                && a.OwnerId == userId
                && a.Status == AssetStatus.Ready
                && a.ContentType.StartsWith("image/"),
            ct
        );
}
