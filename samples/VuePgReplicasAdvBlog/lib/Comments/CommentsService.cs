using Microsoft.EntityFrameworkCore;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Discovery;
using VuePgReplicasAdvBlog.Features.Uploads;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Features.Comments;

public static class CommentsService
{
    public static async Task<CommentPage> CommentsPageAsync(
        Guid id,
        Guid? uid,
        AppDb db,
        Storage storage,
        string? cursor,
        CancellationToken ct
    )
    {
        var author = await db
            .Posts.IgnoreQueryFilters()
            .Where(p => p.Id == id && (p.ArchivedAt == null || p.AuthorId == uid))
            .Select(p => (Guid?)p.AuthorId)
            .FirstOrDefaultAsync(ct);
        if (author is null)
            throw new FeatureException(FailureKind.Missing);
        if (!CursorHelpers.TryCursor(cursor, out var after))
            throw new FeatureException(FailureKind.InvalidInput, "Invalid comment cursor.");
        return await LoadPageAsync(db, storage, id, author.Value, uid, after, ct);
    }

    public static async Task AddCommentAsync(
        Guid id,
        CommentInput input,
        Guid uid,
        AppDb db,
        JobQueue jobs,
        DiscoveryService discovery,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Posts.AnyAsync(p => p.Id == id && p.ArchivedAt == null, ct))
            throw new FeatureException(FailureKind.Missing);
        var comment = new Comment
        {
            PostId = id,
            UserId = uid,
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
    }

    public static async Task DeleteCommentAsync(
        Guid id,
        Guid uid,
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
            return;
        if (uid != c.UserId && uid != c.PostAuthorId)
            throw new FeatureException(FailureKind.AccessDenied);
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
    }

    public static async Task<CommentPage> LoadPageAsync(
        AppDb db,
        Storage storage,
        Guid postId,
        Guid authorId,
        Guid? uid,
        CursorEdge? after,
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
        return new CommentPage(data, rows.Count > 50 ? CursorHelpers.Cursor(data[^1].CreatedAt, data[^1].Id) : null);
    }
}
