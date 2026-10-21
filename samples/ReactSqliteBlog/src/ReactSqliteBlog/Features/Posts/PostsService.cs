using ReactSqliteBlog.Data;
using ReactSqliteBlog.Features.Analytics;
using ReactSqliteBlog.Features.Discovery;
using ReactSqliteBlog.Features.Social;
using ReactSqliteBlog.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ReactSqliteBlog.Features.Posts;

public static partial class PostsService
{
    public static async Task<List<PostCardDto>> LoadCardsAsync(
        AppDb db,
        IQueryable<Post> query,
        Guid? me,
        CancellationToken ct
    )
    {
        var rows = await query
            .Select(p => new
            {
                p.Id,
                p.Title,
                Body = p.Body.Length > 600 ? p.Body.Substring(0, 600) : p.Body,
                p.CoverUrl,
                p.CreatedAt,
                p.LikeCount,
                p.CommentCount,
                p.ViewCount,
                p.ArchivedAt,
                p.AuthorId,
                Author = new AuthorDto(p.AuthorId, p.Author.DisplayName, p.Author.AvatarUrl),
            })
            .ToListAsync(ct);
        var cards = rows.Select(p => new PostCardDto(
                p.Id,
                p.Title,
                Search.Excerpt(p.Body),
                null,
                p.CoverUrl,
                p.CreatedAt,
                p.Author,
                p.LikeCount,
                p.CommentCount,
                p.ViewCount
            )
            {
                ArchivedAt = p.ArchivedAt,
                CanArchive = p.AuthorId == me,
            })
            .ToList();
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
        var topics = await db.PostTopics.Where(t => ids.Contains(t.PostId)).ToListAsync(ct);
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
                    Topics = topics.Where(t => t.PostId == p.Id).Select(t => t.Topic).ToArray(),
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

    internal static async Task<IResult> TrackViewAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        ViewTracker views,
        DiscoveryService discovery,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var me = ctx.User.UserId();
        var post = await db
            .Posts.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.Id == id && ((p.ArchivedAt == null && p.IsPublished) || p.AuthorId == me))
            .Select(p => new { p.AuthorId })
            .FirstOrDefaultAsync(ct);
        if (post is null)
            return Results.NotFound();
        if (
            ctx.Request.Headers.ContainsKey("X-Inertia-Prefetch")
            || ctx.Request.Headers["Purpose"].ToString().Contains("prefetch")
            || ctx.Request.Headers["Sec-Purpose"].ToString().Contains("prefetch")
        )
            return Results.NoContent();
        await views.TrackAsync(ctx, id, post.AuthorId, ct);
        if (me is not null)
            await discovery.MarkSeenAsync(db, me.Value, id, time.GetUtcNow().UtcDateTime, ct);
        return Results.NoContent();
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
}
