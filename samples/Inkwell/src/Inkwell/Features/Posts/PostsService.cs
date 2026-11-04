using static Inkwell.Features.Posts.PostsMappers;
using System.Net;
using System.Text.RegularExpressions;
using Inkwell.Data;
using Inkwell.Features.Analytics;
using Inkwell.Features.Discovery;
using Inkwell.Features.Notifications;
using Inkwell.Infrastructure;
using Inkwell.Jobs;
using Inertia.Net;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Features.Posts;

public sealed class PostsService(
    AppDb db,
    JobQueue jobs,
    TimeProvider time,
    RealtimeEvents realtime,
    DiscoveryService discovery,
    IInertiaCache cache)
{
    const int PageSize = 10;
    const int CommentPageSize = 50;
    const string TopicsError = "Use up to five topics of 1–32 lowercase letters, numbers or hyphens.";

    public async Task<CursorPage<PostCardDto>> RecentAsync(Guid? me, CursorRequest request, string? topic, CancellationToken ct)
    {
        var query = db.Posts.AsNoTracking();
        if (topic is not null)
            query = query.Where(p => db.PostTopics.Any(t => t.PostId == p.Id && t.Topic == topic));
        // Newest first: the EF Core paginator orders by (CreatedAt, Id) itself from a cursor.
        var page = await ProjectCards(query).ToCursorPageAsync(r => r.CreatedAt, r => r.Id, request with { PerPage = PageSize },
            descending: true, ct: ct);
        var items = await EnrichAsync(db, page.Data.Select(ToCard).ToList(), me, ct);
        return Paging.Map(page, items);
    }

    /// <summary>The most used topics on public posts, busiest first.</summary>
    public async Task<List<TopicCount>> PopularTopicsAsync(int take, CancellationToken ct) =>
        await (from t in db.PostTopics.AsNoTracking()
               join p in db.Posts.AsNoTracking() on t.PostId equals p.Id
               group t by t.Topic into g
               orderby g.Count() descending, g.Key
               select new TopicCount(g.Key, g.Count()))
            .Take(take)
            .ToListAsync(ct);

    /// <summary>FTS5 search ranked by bm25, with highlighted snippets. Pages by offset because rank has no keyset.</summary>
    public async Task<Page<PostCardDto>> SearchAsync(Guid? me, string term, PageRequest request, CancellationToken ct)
    {
        var match = Search.ToFtsQuery(term);
        if (match is null)
        {
            var empty = request with { PerPage = PageSize };
            return new Page<PostCardDto>([], empty.Page, empty.PerPage, null, false);
        }

        var page = request with { PerPage = PageSize };
        // The delegate paginator keeps OFFSET paging (bm25 has no keyset) and asks for one extra row for hasMore.
        var result = await Pagination.PaginateAsync(page,
            async (skip, take, token) => (IReadOnlyList<SearchHit>)await db.Database.SqlQuery<SearchHit>($"""
                SELECT p.Id AS Id, snippet(PostsFts, 1, '<mark>', '</mark>', '…', 16) AS Snippet
                FROM PostsFts JOIN Posts p ON p.rowid = PostsFts.rowid
                WHERE PostsFts MATCH {match} AND p.ArchivedAt IS NULL AND p.IsPublished = true
                ORDER BY bm25(PostsFts) LIMIT {take} OFFSET {skip}
                """).ToListAsync(token),
            ct: ct);
        var ids = result.Data.Select(h => h.Id).ToList();
        var cards = (await LoadCardsAsync(db.Posts.AsNoTracking().Where(p => ids.Contains(p.Id)), me, ct)).ToDictionary(c => c.Id);
        var items = result.Data.Where(h => cards.ContainsKey(h.Id))
            .Select(h => cards[h.Id] with { Snippet = Search.SafeSnippet(h.Snippet) })
            .ToList();
        return Paging.Map(result, items);
    }

    /// <summary>The post as the viewer may see it (published, or their own draft/archive); null otherwise.</summary>
    public async Task<PostPage?> GetAsync(Guid id, Guid? me, CancellationToken ct)
    {
        var row = await db.Posts.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == id && ((p.ArchivedAt == null && p.IsPublished) || p.AuthorId == me))
            .Select(p => new
            {
                Dto = new PostDetailDto(p.Id, p.Title, p.Body, p.CoverUrl, p.CreatedAt, p.UpdatedAt,
                    new AuthorDto(p.AuthorId, p.Author.DisplayName, p.Author.AvatarUrl), p.LikeCount, p.CommentCount, p.ViewCount),
                p.IsPublished,
                p.ArchivedAt,
                Liked = me != null && db.PostLikes.Any(l => l.PostId == id && l.UserId == me),
                Bookmarked = me != null && db.Bookmarks.Any(b => b.PostId == id && b.UserId == me),
                Reposted = me != null && db.Reposts.Any(r => r.PostId == id && r.UserId == me),
                RepostCount = db.Reposts.Count(r => r.PostId == id),
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;
        // Member initializers inside an EF projection are dropped, so the extra fields are set here.
        var post = row.Dto with
        {
            IsPublished = row.IsPublished,
            Topics = await TopicsOfAsync(id, ct),
            Bookmarked = row.Bookmarked,
            Reposted = row.Reposted,
            RepostCount = row.RepostCount,
            ArchivedAt = row.ArchivedAt,
            CanArchive = me == row.Dto.Author.Id,
        };
        return new PostPage(post, row.Liked);
    }

    /// <summary>One page of comments for the drawer; null when the viewer cannot see the post.</summary>
    public async Task<CursorPage<CommentDto>?> CommentsPageAsync(Guid id, Guid? me, CursorRequest request, CancellationToken ct)
    {
        var author = await VisibleAuthorAsync(id, me, ct);
        if (author is null) return null;
        return await CommentsAsync(id, me, author.Value, request with { PerPage = CommentPageSize }, ct);
    }

    /// <summary>Unbounded page load (first drawer payload, follow-up JSON pages) — the paginator sizes by request.</summary>
    public async Task<CursorPage<CommentDto>> CommentsAsync(Guid id, Guid? me, Guid postAuthor, CursorRequest request, CancellationToken ct)
    {
        // The keyset runs on the entity query (Order by + subquery projections do not push down after a Select);
        // the page's rows are then enriched with like state in one follow-up query, like the post cards.
        var query = db.Comments.IgnoreQueryFilters().AsNoTracking().Where(c => c.PostId == id);
        var page = await query.ToCursorPageAsync(c => c.CreatedAt, c => c.Id, request, descending: true, ct: ct);
        var ids = page.Data.Select(c => c.Id).ToArray();
        var rows = await db.Comments.IgnoreQueryFilters().AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new CommentDto(c.Id, c.Body, c.CreatedAt,
                new AuthorDto(c.AuthorId, c.Author.DisplayName, c.Author.AvatarUrl),
                me != null && (c.AuthorId == me || postAuthor == me),
                me != null && db.CommentLikes.Any(l => l.CommentId == c.Id && l.UserId == me),
                db.CommentLikes.Count(l => l.CommentId == c.Id)))
            .ToListAsync(ct);
        var byId = rows.ToDictionary(c => c.Id);
        var items = page.Data.Select(c => byId[c.Id]).ToList();
        return Paging.Map(page, items);
    }

    public async Task<PostEditPage?> GetForEditAsync(Guid id, Guid? me, CancellationToken ct)
    {
        var post = await db.Posts.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new { p.AuthorId, p.Id, p.Title, p.Body, p.CoverUrl, p.IsPublished })
            .FirstOrDefaultAsync(ct);
        if (post is null) return null;
        if (post.AuthorId != me) return new PostEditPage(null, Forbidden: true);
        return new PostEditPage(
            new PostEditDto(post.Id, post.Title, post.Body, post.CoverUrl) { Topics = await TopicsOfAsync(id, ct), IsPublished = post.IsPublished },
            Forbidden: false);
    }

    public async Task<Outcome> CreateAsync(PostInput input, Guid userId, CancellationToken ct)
    {
        var topics = PostsValidators.NormalizeTopics(input.Topics);
        if (!PostsValidators.TopicsValid(topics)) return Outcome.Fail("topics", TopicsError);
        if (await CoverInvalidAsync(input.CoverUrl, userId, ct)) return Outcome.Fail("coverUrl", "Choose an image you uploaded.");
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
        db.PostTopics.AddRange(topics.Select(t => new PostTopic { PostId = post.Id, Topic = t }));
        await db.SaveChangesAsync(ct);
        await AttachUploadsAsync(post, ct);
        await realtime.ChangedAsync(post.Id, userId, published: post.IsPublished);
        await InvalidateAnalyticsAsync(userId, post.Id, ct);
        return Outcome.FlashThenRedirect("success", post.IsPublished ? "Post published" : "Draft saved", $"/posts/{post.Id}");
    }

    public async Task<Outcome> UpdateAsync(Guid id, PostInput input, Guid userId, CancellationToken ct)
    {
        var post = await db.Posts.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return Outcome.Missing();
        if (post.AuthorId != userId) return Outcome.Denied();
        if (post.IsPublished && input.IsPublished == false)
            return Outcome.Fail("isPublished", "Published posts cannot become drafts. Archive the post to hide it.");
        var topics = PostsValidators.NormalizeTopics(input.Topics);
        if (!PostsValidators.TopicsValid(topics)) return Outcome.Fail("topics", TopicsError);
        if (await CoverInvalidAsync(input.CoverUrl, userId, ct)) return Outcome.Fail("coverUrl", "Choose an image you uploaded.");

        var wasPublished = post.IsPublished;
        post.Title = input.Title.Trim();
        post.Body = input.Body;
        post.CoverUrl = Clean(input.CoverUrl);
        post.UpdatedAt = time.GetUtcNow().UtcDateTime;
        if (!wasPublished && input.IsPublished == true) post.PublishedAt = post.UpdatedAt;
        post.IsPublished = input.IsPublished ?? wasPublished;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.PostTopics.Where(t => t.PostId == id).ExecuteDeleteAsync(ct);
        db.PostTopics.AddRange(topics.Select(t => new PostTopic { PostId = id, Topic = t }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await AttachUploadsAsync(post, ct);
        await realtime.ChangedAsync(id, userId, published: !wasPublished && post.IsPublished);
        await InvalidateAnalyticsAsync(userId, id, ct);
        return Outcome.FlashThenRedirect("success", "Post updated", $"/posts/{id}");
    }

    public async Task<Outcome> DeleteAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var post = await db.Posts.IgnoreQueryFilters().Where(p => p.Id == id)
            .Select(p => new { p.AuthorId, Public = p.IsPublished && p.ArchivedAt == null })
            .FirstOrDefaultAsync(ct);
        if (post is null) return Outcome.Missing();
        if (post.AuthorId != userId) return Outcome.Denied();
        await db.Posts.IgnoreQueryFilters().Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        await realtime.RemovedAsync(id, post.AuthorId, post.Public);
        await InvalidateAnalyticsAsync(post.AuthorId, id, ct);
        return Outcome.FlashThenRedirect("success", "Post deleted", "/posts");
    }

    // The author's dashboards are cached props; remove both the user-wide and the per-post entries after a structural
    // change. Views, likes and comments keep their minute-long consistency window.
    async Task InvalidateAnalyticsAsync(Guid userId, Guid postId, CancellationToken ct)
    {
        await cache.RemoveByTagAsync(AnalyticsCache.UserTag(userId), ct);
        await cache.RemoveByTagAsync(AnalyticsCache.PostTag(postId), ct);
    }

    public async Task<Outcome> AddCommentAsync(Guid id, CommentInput input, Guid userId, CancellationToken ct)
    {
        var authorId = await db.Posts.Where(p => p.Id == id).Select(p => (Guid?)p.AuthorId).FirstOrDefaultAsync(ct);
        if (authorId is null) return Outcome.Missing();
        var notify = authorId != userId;
        var now = time.GetUtcNow().UtcDateTime;
        var comment = new Comment { PostId = id, AuthorId = userId, Body = input.Body.Trim(), CreatedAt = now };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Comments.Add(comment);
        if (notify)
        {
            jobs.Enqueue(db, new CommentNotification(comment.Id));
            db.Notifications.Add(new Inkwell.Features.Notifications.Notification { UserId = authorId.Value, Title = "Someone commented on your post", Href = $"/posts/{id}", At = now });
        }
        await db.SaveChangesAsync(ct);
        await db.Posts.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.CommentCount, p => p.CommentCount + 1), ct);
        await discovery.MarkSeenAsync(userId, id, now, ct);
        await tx.CommitAsync(ct);
        if (notify) await NotificationsCache.InvalidateAsync(cache, authorId.Value, ct);
        if (notify) jobs.Wake();
        await realtime.ChangedAsync(id, userId, notify ? "Someone commented on your post" : null);
        return Outcome.FlashBack("success", "Comment posted");
    }

    public async Task<Outcome> DeleteCommentAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var comment = await db.Comments.Where(c => c.Id == id)
            .Select(c => new { c.PostId, c.AuthorId, PostAuthorId = c.Post.AuthorId })
            .FirstOrDefaultAsync(ct);
        if (comment is null) return Outcome.Missing();
        if (userId != comment.AuthorId && userId != comment.PostAuthorId) return Outcome.Denied();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var deleted = await db.Comments.Where(c => c.Id == id).ExecuteDeleteAsync(ct) > 0;
        if (deleted)
            await db.Posts.Where(p => p.Id == comment.PostId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CommentCount, p => p.CommentCount > 0 ? p.CommentCount - 1 : 0), ct);
        await discovery.MarkProfileDirtyAsync(comment.AuthorId, time.GetUtcNow().UtcDateTime, ct);
        await tx.CommitAsync(ct);
        if (deleted) await realtime.ChangedAsync(comment.PostId, userId);
        return Outcome.FlashBack("success", "Comment deleted");
    }

    /// <summary>Marks the post seen by the viewer and returns its author, or null when the viewer cannot see it.</summary>
    public async Task<Guid?> MarkViewedAsync(Guid id, Guid? me, CancellationToken ct)
    {
        var author = await VisibleAuthorAsync(id, me, ct);
        if (author is not null && me is { } viewer)
            await discovery.MarkSeenAsync(viewer, id, time.GetUtcNow().UtcDateTime, ct);
        return author;
    }

    public async Task<List<PostCardDto>> LoadCardsAsync(IQueryable<Post> query, Guid? me, CancellationToken ct) =>
        await LoadCardsAsync(db, query, me, ct);

    /// <summary>Card loading on a caller-owned context, for work that runs beside the request's own queries.</summary>
    public async Task<List<PostCardDto>> LoadCardsAsync(AppDb context, IQueryable<Post> query, Guid? me, CancellationToken ct) =>
        await EnrichAsync(context, (await ProjectCards(query).ToListAsync(ct)).Select(r => ToCard(r)).ToList(), me, ct);

    async Task<Guid?> VisibleAuthorAsync(Guid id, Guid? me, CancellationToken ct) =>
        await db.Posts.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == id && ((p.ArchivedAt == null && p.IsPublished) || p.AuthorId == me))
            .Select(p => (Guid?)p.AuthorId)
            .FirstOrDefaultAsync(ct);

    async Task<string[]> TopicsOfAsync(Guid id, CancellationToken ct) =>
        await db.PostTopics.Where(t => t.PostId == id).Select(t => t.Topic).ToArrayAsync(ct);

    /// <summary>Adds topics, viewer flags and repost counts: two queries for the whole list.</summary>
    async Task<List<PostCardDto>> EnrichAsync(AppDb context, List<PostCardDto> cards, Guid? me, CancellationToken ct)
    {
        var ids = cards.Select(c => c.Id).ToArray();
        if (ids.Length == 0) return cards;
        var topics = (await context.PostTopics.AsNoTracking().Where(t => ids.Contains(t.PostId)).ToListAsync(ct))
            .ToLookup(t => t.PostId, t => t.Topic);
        var states = await context.Posts.IgnoreQueryFilters().AsNoTracking()
            .Where(p => ids.Contains(p.Id) && ((p.ArchivedAt == null && p.IsPublished) || p.AuthorId == me))
            .Select(p => new
            {
                p.Id,
                p.ArchivedAt,
                Liked = me != null && context.PostLikes.Any(l => l.UserId == me && l.PostId == p.Id),
                Bookmarked = me != null && context.Bookmarks.Any(b => b.UserId == me && b.PostId == p.Id),
                Reposted = me != null && context.Reposts.Any(r => r.UserId == me && r.PostId == p.Id),
                RepostCount = context.Reposts.Count(r => r.PostId == p.Id),
            })
            .ToDictionaryAsync(p => p.Id, ct);
        return cards.Where(c => states.ContainsKey(c.Id))
            .Select(c =>
            {
                var s = states[c.Id];
                return c with
                {
                    Topics = topics[c.Id].ToArray(),
                    Liked = s.Liked,
                    Bookmarked = s.Bookmarked,
                    Reposted = s.Reposted,
                    RepostCount = s.RepostCount,
                    ArchivedAt = s.ArchivedAt,
                    CanArchive = c.Author.Id == me,
                };
            })
            .ToList();
    }

    /// <summary>Marks the user's uploads referenced by the cover or the markdown body as belonging to the post.</summary>
    async Task AttachUploadsAsync(Post post, CancellationToken ct)
    {
        var urls = Search.MarkdownUrl().Matches(post.Body).Select(m => m.Groups[1].Value).ToList();
        if (post.CoverUrl is not null) urls.Add(post.CoverUrl);
        await db.Uploads.Where(u => u.OwnerId == post.AuthorId && urls.Contains(u.Url))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PostId, post.Id), ct);
    }

    /// <summary>The cover must be one of the user's own uploads.</summary>
    async Task<bool> CoverInvalidAsync(string? coverUrl, Guid userId, CancellationToken ct) =>
        Clean(coverUrl) is { } cover && !await db.Uploads.AnyAsync(u => u.OwnerId == userId && u.Url == cover, ct);

    static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static IQueryable<CardRow> ProjectCards(IQueryable<Post> posts) =>
        posts.Select(p => new CardRow
        {
            Id = p.Id,
            Title = p.Title,
            Body = p.Body.Length > 600 ? p.Body.Substring(0, 600) : p.Body,
            CoverUrl = p.CoverUrl,
            CreatedAt = p.CreatedAt,
            Author = new AuthorDto(p.AuthorId, p.Author.DisplayName, p.Author.AvatarUrl),
            LikeCount = p.LikeCount,
            CommentCount = p.CommentCount,
            ViewCount = p.ViewCount,
            ArchivedAt = p.ArchivedAt,
        });

}
