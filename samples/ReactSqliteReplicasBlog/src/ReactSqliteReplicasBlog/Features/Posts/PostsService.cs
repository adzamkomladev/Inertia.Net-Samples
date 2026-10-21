using System.Security.Claims;
using ReactSqliteReplicasBlog.Data;
using ReactSqliteReplicasBlog.Features.Analytics;
using ReactSqliteReplicasBlog.Features.Discovery;
using ReactSqliteReplicasBlog.Features.Social;
using ReactSqliteReplicasBlog.Infrastructure;
using ReactSqliteReplicasBlog.Jobs;
using Microsoft.EntityFrameworkCore;
using static ReactSqliteReplicasBlog.Features.Posts.PostsHelpers;

namespace ReactSqliteReplicasBlog.Features.Posts;

public static partial class PostsService
{
    /// <summary>The feed page data: items plus everything the handler needs to wrap them in scroll props.</summary>
    public sealed record FeedResult(
        List<PostCardDto> Items,
        string? Term,
        int Page,
        bool HasMore,
        string? Next,
        bool HasSearch,
        bool ExternalSearch,
        bool BadCursor = false,
        bool BadEngine = false
    );

    public static async Task<FeedResult> FeedAsync(
        AppDb db,
        Guid? uid,
        string? q,
        int? page,
        string? engine,
        string? cursor,
        MeilisearchTaskClient? external,
        bool externalSearchEnabled,
        OptionalServiceNamespace names,
        ILogger logger,
        CancellationToken ct
    )
    {
        var term = q?.Trim();
        var pageNo = Math.Max(page ?? 1, 1);
        var hasSearch = term is { Length: > 0 };
        if (!SocialHelpers.TryCursor(cursor, out var after))
            return new FeedResult([], term, pageNo, false, null, hasSearch, false, BadCursor: true);
        if (engine is not (null or "native" or "meilisearch"))
            return new FeedResult([], term, pageNo, false, null, hasSearch, false, BadEngine: true);
        return hasSearch
            ? await SearchFeedAsync(
                db,
                uid,
                term!,
                pageNo,
                engine,
                external,
                externalSearchEnabled,
                names,
                logger,
                ct
            )
            : await RecentFeedAsync(db, uid, pageNo, after, ct);
    }

    static async Task<FeedResult> RecentFeedAsync(
        AppDb db,
        Guid? uid,
        int pageNo,
        SocialService.Edge? after,
        CancellationToken ct
    )
    {
        var query = db.Posts.AsNoTracking();
        if (after is { } edge)
            query = query.Where(x =>
                x.CreatedAt <= edge.At
                && (x.CreatedAt < edge.At || (x.CreatedAt == edge.At && x.Id < edge.Id))
            );
        var rows = await ProjectCards(
                query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            )
            .Take(PageSize + 1)
            .ToListAsync(ct);
        var hasMore = rows.Count > PageSize;
        var items = await EnrichCardsAsync(
            db,
            rows.Take(PageSize).Select(row => ToCard(row)).ToList(),
            uid,
            ct
        );
        return new FeedResult(
            items,
            null,
            pageNo,
            hasMore,
            hasMore && items.Count > 0
                ? SocialHelpers.Cursor(items[^1].CreatedAt, items[^1].Id)
                : null,
            false,
            false
        );
    }

    static async Task<FeedResult> SearchFeedAsync(
        AppDb db,
        Guid? uid,
        string term,
        int pageNo,
        string? engine,
        MeilisearchTaskClient? external,
        bool externalSearchEnabled,
        OptionalServiceNamespace names,
        ILogger logger,
        CancellationToken ct
    )
    {
        var take = PageSize + 1;
        var skip = (pageNo - 1) * PageSize;
        if (
            external is not null
            && externalSearchEnabled
            && engine is not "native"
            && await SearchIndexReadyAsync(db, ct)
        )
        {
            try
            {
                var result = await external.SearchAsync(
                    names.MeilisearchIndex("posts"),
                    term,
                    take,
                    skip,
                    ct
                );
                var hits = result
                    .DocumentIds.Select(ParseHit)
                    .Where(hit => hit is not null)
                    .Cast<SearchHit>()
                    .ToList();
                return await BuildSearchPageAsync(
                    db,
                    uid,
                    term,
                    pageNo,
                    hits,
                    snippets: null,
                    external: true,
                    ct
                );
            }
            catch (Exception ex)
                when (ex is HttpRequestException or System.Text.Json.JsonException
                    || (ex is TaskCanceledException && !ct.IsCancellationRequested)
                )
            {
                logger.LogWarning(ex, "External search unavailable; using database search.");
            }
        }

        if (engine is "meilisearch")
            pageNo = 1;
        var native = await NativeSearchAsync(db, term, take, (pageNo - 1) * PageSize, ct);
        return await BuildSearchPageAsync(
            db,
            uid,
            term,
            pageNo,
            native,
            native.ToDictionary(hit => hit.Id, hit => Search.SafeSnippet(hit.Snippet)),
            external: false,
            ct
        );
    }

    static async Task<FeedResult> BuildSearchPageAsync(
        AppDb db,
        Guid? uid,
        string term,
        int pageNo,
        List<SearchHit> hits,
        Dictionary<Guid, string>? snippets,
        bool external,
        CancellationToken ct
    )
    {
        var hasMore = hits.Count > PageSize;
        var ids = hits.Take(PageSize).Select(hit => hit.Id).ToList();
        var rows = ids.Count == 0
            ? []
            : await ProjectCards(db.Posts.AsNoTracking().Where(post => ids.Contains(post.Id)))
                .ToListAsync(ct);
        var byId = rows.ToDictionary(row => row.Id);
        var items = await EnrichCardsAsync(
            db,
            ids.Where(byId.ContainsKey)
                .Select(id => ToCard(byId[id], snippets))
                .ToList(),
            uid,
            ct
        );
        return new FeedResult(
            items,
            term,
            pageNo,
            hasMore,
            hasMore && items.Count > 0
                ? SocialHelpers.Cursor(items[^1].CreatedAt, items[^1].Id)
                : null,
            true,
            external
        );
    }

    static async Task<bool> SearchIndexReadyAsync(AppDb db, CancellationToken ct) =>
        !await db.SearchIndexStates.AnyAsync(
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
        );

    static Task<List<SearchHit>> NativeSearchAsync(
        AppDb db,
        string term,
        int take,
        int skip,
        CancellationToken ct
    )
    {
        var match = Search.ToFtsQuery(term);
        return match is null
            ? Task.FromResult<List<SearchHit>>([])
            : db
                .Database.SqlQuery<SearchHit>(
                    $"""
                    SELECT p.Id AS Id, snippet(PostsFts, 1, '<mark>', '</mark>', '…', 16) AS Snippet
                    FROM PostsFts JOIN Posts p ON p.rowid = PostsFts.rowid
                    WHERE PostsFts MATCH {match} AND p.ArchivedAt IS NULL AND p.IsPublished = true ORDER BY bm25(PostsFts) LIMIT {take} OFFSET {skip}
                    """
                )
                .ToListAsync(ct);
    }

    static SearchHit? ParseHit(string value) =>
        Guid.TryParse(value, out var id) ? new SearchHit { Id = id } : null;

    static IQueryable<CardRow> ProjectCards(IQueryable<Post> posts) =>
        posts.Select(x => new CardRow
        {
            Id = x.Id,
            Title = x.Title,
            Body = x.Body.Length > 600 ? x.Body.Substring(0, 600) : x.Body,
            CoverUrl = x.CoverUrl,
            CreatedAt = x.CreatedAt,
            Author = new AuthorDto(x.AuthorId, x.Author.DisplayName, x.Author.AvatarUrl),
            LikeCount = x.LikeCount,
            CommentCount = x.CommentCount,
            ViewCount = x.ViewCount,
            ArchivedAt = x.ArchivedAt,
        });

    static PostCardDto ToCard(CardRow row, Dictionary<Guid, string>? snippets = null) =>
        new(
            row.Id,
            row.Title,
            Search.Excerpt(row.Body),
            snippets?.GetValueOrDefault(row.Id),
            row.CoverUrl,
            row.CreatedAt,
            row.Author,
            row.LikeCount,
            row.CommentCount,
            row.ViewCount
        )
        {
            ArchivedAt = row.ArchivedAt,
        };

    sealed class CardRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public string? CoverUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public AuthorDto Author { get; set; } = null!;
        public int LikeCount { get; set; }
        public int CommentCount { get; set; }
        public int ViewCount { get; set; }
        public DateTime? ArchivedAt { get; set; }
    }

    /// <summary>A post page's data, or null when the post does not exist for this viewer.</summary>
    public sealed record PostViewResult(PostDetailDto Post, bool Liked, bool CanEdit, Guid AuthorId);

    public static async Task<PostViewResult?> PostViewAsync(
        AppDb db,
        Guid id,
        Guid? me,
        CancellationToken ct
    )
    {
        var post = await db
            .Posts.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.Id == id && ((p.ArchivedAt == null && p.IsPublished) || p.AuthorId == me))
            .Select(p => new
            {
                p.AuthorId,
                p.ArchivedAt,
                Topics = db.PostTopics.Where(t => t.PostId == id).Select(t => t.Topic).ToArray(),
                Bookmarked =
                    me != null && db.Bookmarks.Any(b => b.PostId == id && b.UserId == me),
                Reposted = me != null && db.Reposts.Any(r => r.PostId == id && r.UserId == me),
                RepostCount = db.Reposts.Count(r => r.PostId == id),
                Liked = me != null && db.PostLikes.Any(l => l.PostId == id && l.UserId == me),
                Dto = new PostDetailDto(
                    p.Id,
                    p.Title,
                    p.Body,
                    p.CoverUrl,
                    p.CreatedAt,
                    p.UpdatedAt,
                    new AuthorDto(p.AuthorId, p.Author.DisplayName, p.Author.AvatarUrl),
                    p.LikeCount,
                    p.CommentCount,
                    p.ViewCount
                )
                {
                    IsPublished = p.IsPublished,
                },
            })
            .FirstOrDefaultAsync(ct);
        if (post is null)
            return null;
        var dto = post.Dto with
        {
            Topics = post.Topics,
            Bookmarked = post.Bookmarked,
            Reposted = post.Reposted,
            RepostCount = post.RepostCount,
            ArchivedAt = post.ArchivedAt,
            CanArchive = me == post.AuthorId,
        };
        return new PostViewResult(dto, post.Liked, me == post.AuthorId, post.AuthorId);
    }

    /// <summary>The comment drawer page, or flags the handler turns into status codes.</summary>
    public sealed record CommentsPageResult(
        List<CommentDto> Data,
        string? NextCursor,
        bool NotFound = false,
        bool BadCursor = false
    );

    public static async Task<CommentsPageResult> CommentsPageAsync(
        AppDb db,
        Guid id,
        Guid? me,
        string? cursor,
        CancellationToken ct
    )
    {
        if (!SocialHelpers.TryCursor(cursor, out var after))
            return new CommentsPageResult([], null, BadCursor: true);
        var author = await db
            .Posts.IgnoreQueryFilters()
            .Where(p => p.Id == id && ((p.ArchivedAt == null && p.IsPublished) || p.AuthorId == me))
            .Select(p => (Guid?)p.AuthorId)
            .FirstOrDefaultAsync(ct);
        if (author is null)
            return new CommentsPageResult([], null, NotFound: true);
        var rows = await LoadCommentsAsync(db, id, me, author.Value, after, 51, ct);
        var more = rows.Count > 50;
        rows = rows.Take(50).ToList();
        return new CommentsPageResult(rows, more ? rows[^1].Cursor : null);
    }

    /// <summary>The edit form data: null when missing, Forbidden set when the viewer is not the author.</summary>
    public sealed record EditView(PostEditDto? Post, bool Forbidden);

    public static async Task<EditView?> PostForEditAsync(
        AppDb db,
        Guid id,
        Guid? userId,
        CancellationToken ct
    )
    {
        var post = await db
            .Posts.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.AuthorId,
                Dto = new PostEditDto(x.Id, x.Title, x.Body, x.CoverUrl)
                {
                    IsPublished = x.IsPublished,
                },
                Topics = db.PostTopics.Where(t => t.PostId == id).Select(t => t.Topic).ToArray(),
            })
            .FirstOrDefaultAsync(ct);
        if (post is null)
            return null;
        if (post.AuthorId != userId)
            return new EditView(null, Forbidden: true);
        return new EditView(post.Dto with { Topics = post.Topics }, Forbidden: false);
    }

    public static async Task<Outcome> CreateAsync(
        PostInput input,
        Guid userId,
        AppDb db,
        JobQueue queue,
        TimeProvider time,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        if (SocialHelpers.TopicsError(input.Topics) is { })
            return Outcome.Fail(
                "topics",
                "Use up to five topics of 1–32 lowercase letters, numbers or hyphens."
            );
        if (await CoverInvalid(db, input.CoverUrl, userId, ct))
            return CoverOutcome();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        var post = new Post
        {
            AuthorId = userId,
            Title = input.Title.Trim(),
            Body = input.Body,
            CoverUrl = Clean(input.CoverUrl),
            CreatedAt = now,
            PublishedAt = now,
            UpdatedAt = now,
            IsPublished = input.IsPublished ?? true,
        };
        db.Posts.Add(post);
        await db.SaveChangesAsync(ct);
        await AttachUploads(post, userId, db, ct);
        await SaveTopics(db, post.Id, input.Topics, ct);
        await SearchIndexing.RequestAsync(db, queue, post.Id, post.IndexVersion, ct);
        await tx.CommitAsync(ct);
        queue.Wake();
        await realtime.ChangedAsync(post.Id, userId, published: post.IsPublished);
        return Outcome.FlashThenRedirect(
            "success",
            post.IsPublished ? "Post published" : "Draft saved",
            $"/posts/{post.Id}"
        );
    }

    public static async Task<Outcome> UpdateAsync(
        Guid id,
        PostInput input,
        Guid userId,
        AppDb db,
        JobQueue queue,
        TimeProvider time,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var post = await db.Posts.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null)
            return Outcome.Missing();
        if (post.AuthorId != userId)
            return Outcome.Denied();
        if (post.IsPublished && input.IsPublished == false)
            return Outcome.Fail(
                "isPublished",
                "Published posts cannot become drafts. Archive the post to hide it."
            );
        if (SocialHelpers.TopicsError(input.Topics) is { })
            return Outcome.Fail(
                "topics",
                "Use up to five topics of 1–32 lowercase letters, numbers or hyphens."
            );
        if (await CoverInvalid(db, input.CoverUrl, userId, ct))
            return CoverOutcome();
        var wasPublished = post.IsPublished;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        post.IndexVersion++;
        post.Title = input.Title.Trim();
        post.Body = input.Body;
        post.CoverUrl = Clean(input.CoverUrl);
        post.UpdatedAt = time.GetUtcNow().UtcDateTime;
        if (!post.IsPublished && input.IsPublished == true)
            post.PublishedAt = post.UpdatedAt;
        post.IsPublished = input.IsPublished ?? post.IsPublished;
        await db.SaveChangesAsync(ct);
        await AttachUploads(post, userId, db, ct);
        await SaveTopics(db, id, input.Topics, ct);
        await SearchIndexing.RequestAsync(db, queue, id, post.IndexVersion, ct);
        await tx.CommitAsync(ct);
        queue.Wake();
        if (!wasPublished && post.IsPublished)
            await realtime.ChangedAsync(id, userId, published: true);
        else
            await realtime.ChangedAsync(id, userId);
        return Outcome.FlashThenRedirect("success", "Post updated", $"/posts/{id}");
    }

    public static async Task<Outcome> DeleteAsync(
        Guid id,
        Guid userId,
        AppDb db,
        JobQueue queue,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var post = await db.Posts.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null)
            return Outcome.Missing();
        if (post.AuthorId != userId)
            return Outcome.Denied();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Posts.Remove(post);
        await db.SaveChangesAsync(ct);
        await SearchIndexing.RequestAsync(db, queue, id, post.IndexVersion + 1, ct);
        await tx.CommitAsync(ct);
        queue.Wake();
        await realtime.RemovedAsync(id, post.AuthorId, post.IsPublished && post.ArchivedAt is null);
        return Outcome.FlashThenRedirect("success", "Post deleted", "/posts");
    }

    public static async Task<Outcome> ToggleLikeAsync(
        Guid id,
        Guid uid,
        AppDb db,
        JobQueue queue,
        DiscoveryService discovery,
        TimeProvider time,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var author = await db
            .Posts.Where(p => p.Id == id)
            .Select(p => (Guid?)p.AuthorId)
            .FirstOrDefaultAsync(ct);
        if (author is null)
            return Outcome.Missing();
        var now = time.GetUtcNow().UtcDateTime;
        var removed = await db
            .PostLikes.Where(l => l.PostId == id && l.UserId == uid)
            .ExecuteDeleteAsync(ct);
        var added =
            removed == 0
                ? await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""INSERT INTO "PostLikes" ("PostId","UserId","CreatedAt") SELECT {id},{uid},{now} WHERE EXISTS (SELECT 1 FROM "Posts" WHERE "Id" = {id} AND "ArchivedAt" IS NULL AND "IsPublished" = true) ON CONFLICT DO NOTHING""",
                    ct
                )
                : 0;
        if (removed > 0 || added > 0)
        {
            await db
                .Posts.Where(p => p.Id == id)
                .ExecuteUpdateAsync(
                    s =>
                        s.SetProperty(
                            p => p.LikeCount,
                            p =>
                                added > 0
                                    ? p.LikeCount + 1
                                    : (p.LikeCount > 0 ? p.LikeCount - 1 : 0)
                        ),
                    ct
                );
            await discovery.MarkProfileDirtyAsync(db, uid, now, ct);
        }

        var notify = added > 0 && author != uid;
        if (notify)
            queue.Enqueue(db, new LikeNotification(id, uid));
        if (added > 0)
            await discovery.MarkSeenAsync(db, uid, id, now, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (notify)
            queue.Wake();
        if (added > 0)
            await realtime.ChangedAsync(id, uid, author != uid ? "Someone liked your post" : null);
        else if (removed > 0)
            await realtime.ChangedAsync(id, uid);
        return Outcome.Ok();
    }

    public static async Task<Outcome> AddCommentAsync(
        Guid id,
        CommentInput input,
        Guid userId,
        AppDb db,
        JobQueue queue,
        DiscoveryService discovery,
        TimeProvider time,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var authorId = await db
            .Posts.Where(x => x.Id == id)
            .Select(x => (Guid?)x.AuthorId)
            .FirstOrDefaultAsync(ct);
        if (authorId is null)
            return Outcome.Missing();
        var comment = new Comment
        {
            PostId = id,
            AuthorId = userId,
            Body = input.Body.Trim(),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        var notify = authorId != userId;
        if (notify)
        {
            queue.Enqueue(db, new CommentNotification(comment.Id));
            await db.SaveChangesAsync(ct);
        }

        await db
            .Posts.Where(x => x.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(p => p.CommentCount, p => p.CommentCount + 1),
                ct
            );
        await discovery.MarkSeenAsync(db, userId, id, time.GetUtcNow().UtcDateTime, ct);
        await tx.CommitAsync(ct);
        if (notify)
            queue.Wake();
        await realtime.ChangedAsync(id, userId, notify ? "Someone commented on your post" : null);
        return Outcome.FlashBack("success", "Comment posted");
    }

    public static async Task<Outcome> DeleteCommentAsync(
        Guid id,
        Guid userId,
        AppDb db,
        DiscoveryService discovery,
        TimeProvider time,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var c = await db
            .Comments.Where(x => x.Id == id)
            .Select(x => new
            {
                x.PostId,
                x.AuthorId,
                PostAuthorId = x.Post.AuthorId,
            })
            .FirstOrDefaultAsync(ct);
        if (c is null)
            return Outcome.Missing();
        if (userId != c.AuthorId && userId != c.PostAuthorId)
            return Outcome.Denied();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var deleted = await db.Comments.Where(x => x.Id == id).ExecuteDeleteAsync(ct) > 0;
        if (deleted)
            await db
                .Posts.Where(x => x.Id == c.PostId)
                .ExecuteUpdateAsync(
                    s =>
                        s.SetProperty(
                            p => p.CommentCount,
                            p => p.CommentCount > 0 ? p.CommentCount - 1 : 0
                        ),
                    ct
                );
        await discovery.MarkProfileDirtyAsync(db, c.AuthorId, time.GetUtcNow().UtcDateTime, ct);
        await tx.CommitAsync(ct);
        if (deleted)
            await realtime.ChangedAsync(c.PostId, userId);
        return Outcome.FlashBack("success", "Comment deleted");
    }

    public static async Task<List<PostCardDto>> LoadCardsAsync(
        AppDb db,
        IQueryable<Post> query,
        Guid? me,
        CancellationToken ct
    )
    {
        var cards = (await ProjectCards(query).ToListAsync(ct)).Select(row => ToCard(row)).ToList();
        return await EnrichCardsAsync(db, cards, me, ct);
    }

    /// <summary>Adds topics, viewer flags (bookmark/repost/like) and repost counts in one round-trip per list.</summary>
    internal static async Task<List<PostCardDto>> EnrichCardsAsync(
        AppDb db,
        List<PostCardDto> cards,
        Guid? me,
        CancellationToken ct
    )
    {
        var ids = cards.Select(p => p.Id).ToArray();
        if (ids.Length == 0)
            return cards;
        var topics = (
            await db
                .PostTopics.AsNoTracking()
                .Where(t => ids.Contains(t.PostId))
                .ToListAsync(ct)
        ).ToLookup(t => t.PostId, t => t.Topic);
        var states = await db
            .Posts.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p =>
                ids.Contains(p.Id) && ((p.ArchivedAt == null && p.IsPublished) || p.AuthorId == me)
            )
            .Select(p => new
            {
                p.Id,
                p.ArchivedAt,
                Liked = me != null && db.PostLikes.Any(l => l.UserId == me && l.PostId == p.Id),
                Bookmarked = me != null
                    && db.Bookmarks.Any(b => b.UserId == me && b.PostId == p.Id),
                Reposted = me != null && db.Reposts.Any(r => r.UserId == me && r.PostId == p.Id),
                RepostCount = db.Reposts.Count(r => r.PostId == p.Id),
            })
            .ToDictionaryAsync(p => p.Id, ct);
        return cards
            .Where(p => states.ContainsKey(p.Id))
            .Select(p =>
                p with
                {
                    Topics = topics[p.Id].ToArray(),
                    Liked = states[p.Id].Liked,
                    Bookmarked = states[p.Id].Bookmarked,
                    Reposted = states[p.Id].Reposted,
                    RepostCount = states[p.Id].RepostCount,
                    ArchivedAt = states[p.Id].ArchivedAt,
                    CanArchive = p.Author.Id == me,
                }
            )
            .ToList();
    }

    /// <summary>Marks the post seen and returns its author, or null when the viewer cannot see it.</summary>
    public static async Task<Guid?> TrackViewAsync(
        AppDb db,
        Guid id,
        Guid? me,
        DiscoveryService discovery,
        DateTime now,
        CancellationToken ct
    )
    {
        var author = await db
            .Posts.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.Id == id && ((p.ArchivedAt == null && p.IsPublished) || p.AuthorId == me))
            .Select(p => (Guid?)p.AuthorId)
            .FirstOrDefaultAsync(ct);
        if (author is null)
            return null;
        if (me is { } viewer)
            await discovery.MarkSeenAsync(db, viewer, id, now, ct);
        return author;
    }

    internal static Task<List<CommentDto>> LoadCommentsAsync(
        AppDb db,
        Guid id,
        Guid? me,
        Guid author,
        SocialService.Edge? after,
        int size,
        CancellationToken ct
    )
    {
        var query = db.Comments.IgnoreQueryFilters().AsNoTracking().Where(c => c.PostId == id);
        if (after is { } e)
            query = query.Where(c =>
                c.CreatedAt <= e.At && (c.CreatedAt < e.At || (c.CreatedAt == e.At && c.Id < e.Id))
            );
        return query
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Take(size)
            .Select(c => new CommentDto(
                c.Id,
                c.Body,
                c.CreatedAt,
                new AuthorDto(c.AuthorId, c.Author.DisplayName, c.Author.AvatarUrl),
                me != null && (c.AuthorId == me || author == me)
            )
            {
                Liked = db.CommentLikes.Any(l => l.CommentId == c.Id && l.UserId == me),
                LikeCount = db.CommentLikes.Count(l => l.CommentId == c.Id),
            })
            .ToListAsync(ct);
    }

    internal static async Task SaveTopics(AppDb db, Guid id, string[]? topics, CancellationToken ct)
    {
        await db.PostTopics.Where(t => t.PostId == id).ExecuteDeleteAsync(ct);
        db.PostTopics.AddRange(
            SocialHelpers
                .NormalizeTopics(topics)
                .Select(t => new PostTopic { PostId = id, Topic = t })
        );
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Marks the user's uploads referenced by the cover or the markdown body as belonging to the post.</summary>
    internal static async Task AttachUploads(Post post, Guid userId, AppDb db, CancellationToken ct)
    {
        var urls = Search.MarkdownUrl().Matches(post.Body).Select(m => m.Groups[1].Value).ToList();
        if (post.CoverUrl is not null)
            urls.Add(post.CoverUrl);
        await db
            .Uploads.Where(u => u.OwnerId == userId && urls.Contains(u.Url))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PostId, post.Id), ct);
    }

    /// <summary>The cover must be one of the user's own uploads; anything else is a validation error on coverUrl.</summary>
    internal static async Task<bool> CoverInvalid(
        AppDb db,
        string? coverUrl,
        Guid userId,
        CancellationToken ct
    )
    {
        var cover = Clean(coverUrl);
        return cover is not null
            && !await db.Uploads.AnyAsync(u => u.OwnerId == userId && u.Url == cover, ct);
    }

    internal static Outcome CoverOutcome() =>
        Outcome.Fail("coverUrl", "Choose an image you uploaded.");
}
