using Microsoft.EntityFrameworkCore;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Discovery;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Features.Likes;

public static class LikesService
{
    public static async Task SetPostLikeAsync(
        Guid id,
        Guid uid,
        bool? desiredState,
        AppDb db,
        JobQueue jobs,
        DiscoveryService discovery,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var authorId = await db
            .Posts.Where(p => p.Id == id && p.ArchivedAt == null)
            .Select(p => (Guid?)p.AuthorId)
            .FirstOrDefaultAsync(ct);
        if (authorId is null)
            throw new FeatureException(FailureKind.Missing);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var liked = false;
        var changed = false;
        var active = desiredState ?? !await db.Likes.AnyAsync(l => l.PostId == id && l.UserId == uid, ct);
        if (!active)
        {
            if (
                await db.Likes.Where(l => l.PostId == id && l.UserId == uid).ExecuteDeleteAsync(ct)
                > 0
            )
            {
                changed = true;
                await db
                    .Posts.Where(p => p.Id == id)
                    .ExecuteUpdateAsync(
                        s =>
                            s.SetProperty(
                                p => p.LikeCount,
                                p => p.LikeCount > 0 ? p.LikeCount - 1 : 0
                            ),
                        ct
                    );
            }

            await discovery.MarkProfileDirtyAsync(db, uid, DateTime.UtcNow, ct);
        }
        else
        {
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"Likes\" (\"PostId\", \"UserId\", \"CreatedAt\") SELECT {id}, {uid}, {DateTime.UtcNow} WHERE EXISTS (SELECT 1 FROM \"Posts\" WHERE \"Id\" = {id} AND \"ArchivedAt\" IS NULL) ON CONFLICT DO NOTHING",
                ct
            );
            if (inserted > 0)
            {
                liked = true;
                changed = true;
                await db
                    .Posts.Where(p => p.Id == id)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(p => p.LikeCount, p => p.LikeCount + 1),
                        ct
                    );
                if (authorId != uid)
                    await jobs.EnqueueAsync(db, new NotifyLike(id, uid), ct: ct);
            }

            await discovery.MarkSeenAsync(db, uid, id, DateTime.UtcNow, ct);
        }

        await tx.CommitAsync(ct);
        if (changed)
            await realtime.ChangedAsync(
                id,
                liked ? uid.ToString() : null,
                liked ? "Someone liked your post" : null
            );
    }
}
