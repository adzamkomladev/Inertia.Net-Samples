using Microsoft.EntityFrameworkCore;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Discovery;
using VuePgReplicasAdvBlog.Features.Search;
using VuePgReplicasAdvBlog.Features.Uploads;
using VuePgReplicasAdvBlog.Infrastructure;
using static VuePgReplicasAdvBlog.Features.Posts.PostsHelpers;

namespace VuePgReplicasAdvBlog.Features.Posts;

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

    public static async Task<List<PostSummary>> LoadSummariesAsync(
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

    public static async Task<PostFeed> FeedAsync(
        Guid? uid,
        AppDb db,
        Storage storage,
        string? q,
        int? page,
        string? engine,
        string? cursor,
        MeilisearchTaskClient? external,
        bool useExternalSearch,
        OptionalServiceNamespace serviceNamespace,
        CancellationToken ct
    )
    {
        var size = uid is { } u
            ? await db
                .Users.Where(x => x.Id == u)
                .Select(x => x.Preferences.PostsPerPage)
                .FirstOrDefaultAsync(ct)
            : 0;
        if (size <= 0)
            size = 10;
        if (engine is not (null or "native" or "meilisearch"))
            throw new FeatureException(FailureKind.InvalidInput, "Invalid search engine.");
        var pageNo = Math.Max(page ?? 1, 1);
        var skip = (pageNo - 1) * size;
        if (!CursorHelpers.TryCursor(cursor, out var after))
            throw new FeatureException(FailureKind.InvalidInput, "Invalid cursor.");
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
            if (
                external is not null
                && useExternalSearch
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
                    var result = await external.SearchAsync(
                        serviceNamespace.MeilisearchIndex("posts"),
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
                    when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                { }
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
        return new PostFeed(items, pageNo, hasMore, cursor,
            hasMore && items.Count > 0 ? CursorHelpers.Cursor(items[^1].CreatedAt, items[^1].Id) : null,
            match is not null, usedExternalSearch ? "meilisearch" : "native");
    }

    public static async Task<string> CreateAsync(
        PostInput input,
        Guid uid,
        AppDb db,
        JobQueue jobs,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        if (PostsHelpers.TopicsError(input.Topics) is { } error)
            throw FeatureException.Invalid("topics", error);
        if (await CoverInvalid(db, input.CoverAssetId, uid, ct))
            throw FeatureException.Invalid("coverAssetId", "Upload an image first.");
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

            foreach (var topic in PostsHelpers.NormalizeTopics(input.Topics))
                db.PostTopics.Add(new PostTopic { PostId = post.Id, Topic = topic });
            await db.SaveChangesAsync(ct);
            await SearchIndexing.RequestAsync(db, jobs, post.Id, post.IndexVersion, ct);
            await tx.CommitAsync(ct);
            publishedId = post.Id;
            break;
        }

        await realtime.ChangedAsync(publishedId, published: true);
        return slug;
    }

    public static async Task<PostDetail> ShowAsync(string slug, Guid? uid, AppDb db, Storage storage, CancellationToken ct) =>
        await LoadDetail(db, storage, p => p.Slug == slug, uid, ct) ?? throw new FeatureException(FailureKind.Missing);

    public static async Task<PostDetail> EditAsync(string slug, Guid uid, AppDb db, Storage storage, CancellationToken ct)
    {
        var detail = await ShowAsync(slug, uid, db, storage, ct);
        if (!detail.CanEdit) throw new FeatureException(FailureKind.AccessDenied);
        return detail;
    }

    public static async Task<string> UpdateAsync(
        Guid id,
        PostInput input,
        Guid uid,
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
            throw new FeatureException(FailureKind.Missing);
        if (post.AuthorId != uid)
            throw new FeatureException(FailureKind.AccessDenied);
        if (PostsHelpers.TopicsError(input.Topics) is { } error)
            throw FeatureException.Invalid("topics", error);
        if (await CoverInvalid(db, input.CoverAssetId, uid, ct))
            throw FeatureException.Invalid("coverAssetId", "Upload an image first.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var oldCover = input.CoverAssetId == post.CoverAssetId ? null : post.Cover;
        post.Title = input.Title!.Trim();
        post.Body = input.Body!;
        post.Excerpt = MakeExcerpt(input.Body!);
        post.CoverAssetId = input.CoverAssetId;
        post.UpdatedAt = DateTime.UtcNow;
        post.IndexVersion++;
        await db.PostTopics.Where(t => t.PostId == id).ExecuteDeleteAsync(ct);
        foreach (var topic in PostsHelpers.NormalizeTopics(input.Topics))
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
        return post.Slug;
    }

    public static async Task<Guid> TrackViewAsync(Guid id, Guid? uid, AppDb db, DiscoveryService discovery, CancellationToken ct)
    {
        var authorId = await db.Posts.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == id && (p.ArchivedAt == null || p.AuthorId == uid))
            .Select(p => (Guid?)p.AuthorId).FirstOrDefaultAsync(ct);
        if (authorId is not { } author) throw new FeatureException(FailureKind.Missing);
        if (uid is { } viewer)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await discovery.MarkSeenAsync(db, viewer, id, DateTime.UtcNow, ct);
            await tx.CommitAsync(ct);
        }
        return author;
    }

    public static async Task DeleteAsync(
        Guid id,
        Guid uid,
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
            throw new FeatureException(FailureKind.Missing);
        if (post.AuthorId != uid)
            throw new FeatureException(FailureKind.AccessDenied);
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
