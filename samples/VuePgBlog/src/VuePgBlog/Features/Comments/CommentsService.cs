using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Features.Discovery;
using VuePgBlog.Infrastructure;

namespace VuePgBlog.Features;

public static partial class CommentsService
{
    internal static async Task<IResult> CommentsPageAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        Storage storage,
        string? cursor,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserIdOrNull();
        var author = await db
            .Posts.IgnoreQueryFilters()
            .Where(p => p.Id == id && (p.ArchivedAt == null || p.AuthorId == uid))
            .Select(p => (Guid?)p.AuthorId)
            .FirstOrDefaultAsync(ct);
        if (author is null)
            return Results.NotFound();
        if (!SocialHelpers.TryCursor(cursor, out var after))
            return Results.BadRequest("Invalid comment cursor.");
        return Results.Json(await LoadPageAsync(db, storage, id, author.Value, uid, after, ct));
    }

    internal static async Task<IResult> AddCommentAsync(
        Guid id,
        CommentInput input,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        DiscoveryService discovery,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Posts.AnyAsync(p => p.Id == id && p.ArchivedAt == null, ct))
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        var comment = new Comment
        {
            PostId = id,
            UserId = ctx.User.UserId(),
            Body = input.Body!.Trim(),
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        await db
            .Posts.Where(p => p.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(p => p.CommentCount, p => p.CommentCount + 1),
                ct
            );
        await discovery.MarkSeenAsync(db, comment.UserId, id, DateTime.UtcNow, ct);
        await jobs.EnqueueAsync(db, new NotifyComment(comment.Id), ct: ct);
        await tx.CommitAsync(ct);
        await realtime.ChangedAsync(id, comment.UserId.ToString(), "New comment on your post");
        return Back();
    }

    internal static async Task<IResult> DeleteCommentAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        DiscoveryService discovery,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var c = await db
            .Comments.Where(x => x.Id == id)
            .Select(x => new
            {
                x.PostId,
                x.UserId,
                PostAuthorId = x.Post.AuthorId,
            })
            .FirstOrDefaultAsync(ct);
        if (c is null)
            return Back();
        var uid = ctx.User.UserId();
        if (uid != c.UserId && uid != c.PostAuthorId)
            return HttpExtensions.Forbidden();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var removed = await db.Comments.Where(x => x.Id == id).ExecuteDeleteAsync(ct) > 0;
        if (removed)
            await db
                .Posts.Where(p => p.Id == c.PostId)
                .ExecuteUpdateAsync(
                    s =>
                        s.SetProperty(
                            p => p.CommentCount,
                            p => p.CommentCount > 0 ? p.CommentCount - 1 : 0
                        ),
                    ct
                );
        await discovery.MarkProfileDirtyAsync(db, c.UserId, DateTime.UtcNow, ct);
        await tx.CommitAsync(ct);
        if (removed)
            await realtime.ChangedAsync(c.PostId);
        return Back();
    }

    internal static async Task<object> LoadPageAsync(
        AppDb db,
        Storage storage,
        Guid postId,
        Guid authorId,
        Guid? uid,
        SocialService.Edge? after,
        CancellationToken ct
    )
    {
        var query = db.Comments.IgnoreQueryFilters().AsNoTracking().Where(c => c.PostId == postId);
        if (after is { } edge)
            query = query.Where(c =>
                c.CreatedAt <= edge.At
                && (c.CreatedAt < edge.At || (c.CreatedAt == edge.At && c.Id < edge.Id))
            );
        var rows = await query
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Take(51)
            .Select(c => new
            {
                c.Id,
                c.Body,
                c.CreatedAt,
                c.UserId,
                Name = c.User.Name,
                AvatarKey = db
                    .Assets.Where(a => a.Id == c.User.AvatarAssetId)
                    .Select(a => a.Key)
                    .FirstOrDefault(),
                Liked = uid != null
                    && db.CommentLikes.Any(l => l.CommentId == c.Id && l.UserId == uid),
                LikeCount = db.CommentLikes.Count(l => l.CommentId == c.Id),
            })
            .ToListAsync(ct);
        var data = rows.Take(50)
            .Select(c => new CommentItem(
                c.Id,
                c.Body,
                c.CreatedAt,
                new AuthorDto(
                    c.UserId,
                    c.Name,
                    c.AvatarKey is null ? null : storage.Url(c.AvatarKey)
                ),
                uid == c.UserId || uid == authorId,
                c.Liked,
                c.LikeCount
            ))
            .ToList();
        return new
        {
            data,
            nextCursor = rows.Count > 50
                ? SocialHelpers.Cursor(data[^1].CreatedAt, data[^1].Id)
                : null,
        };
    }
}
