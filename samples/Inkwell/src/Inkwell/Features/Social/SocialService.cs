using System.Text;
using Microsoft.EntityFrameworkCore;
using Inkwell.Data;
using Inkwell.Features.Discovery;
using Inkwell.Jobs;
using Inkwell.Features.Posts;
using Inkwell.Infrastructure;
using static Inkwell.Features.Social.SocialHelpers;

namespace Inkwell.Features.Social;

public static partial class SocialService
{
    /// <summary>Whether the post-like command found its target.</summary>
    public sealed record PostLikeResult(bool Missing = false);
    /// <summary>Whether the follow command hit a guard (self-follow or unknown user).</summary>
    public sealed record FollowResult(bool SelfFollow = false, bool UserMissing = false);
    /// <summary>Whether the comment-like command found its target.</summary>
    public sealed record CommentLikeResult(bool Missing = false);
    /// <summary>Whether the post-state (bookmark/repost/archive) command found its target.</summary>
    public sealed record PostStateResult(bool NotFound = false, bool Forbidden = false);
    /// <summary>The ranked /feed page data.</summary>
    public sealed record FeedData(
        List<PostCardDto> Items,
        string? NextCursor,
        string Mode,
        string Title,
        bool BadMode = false,
        bool BadCursor = false,
        bool Gone = false
    );
    /// <summary>The timeline (mine/archives/bookmarks/user) page data.</summary>
    public sealed record TimelineData(List<PostCardDto> Items, string? Next, bool BadCursor = false);
    /// <summary>The /users directory page data.</summary>
    public sealed record DirectoryData(List<UserSummary> Users, string? Next, bool BadCursor = false);

    internal static async Task<PostLikeResult> SetPostLikeAsync(
        Guid id,
        Guid uid,
        bool active,
        AppDb db,
        JobQueue jobs,
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
            return new PostLikeResult(Missing: true);
        var now = time.GetUtcNow().UtcDateTime;
        var changed = active
            ? await db.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "PostLikes" ("PostId", "UserId", "CreatedAt") SELECT {id}, {uid}, {now} WHERE EXISTS (SELECT 1 FROM "Posts" WHERE "Id" = {id} AND "ArchivedAt" IS NULL AND "IsPublished" = true) ON CONFLICT DO NOTHING""",
                ct
            )
            : await db
                .PostLikes.Where(l => l.PostId == id && l.UserId == uid)
                .ExecuteDeleteAsync(ct);
        if (changed > 0)
        {
            await db
                .Posts.Where(p => p.Id == id)
                .ExecuteUpdateAsync(
                    s =>
                        s.SetProperty(
                            p => p.LikeCount,
                            p => active ? p.LikeCount + 1 : (p.LikeCount > 0 ? p.LikeCount - 1 : 0)
                        ),
                    ct
                );
            if (active && author != uid)
                jobs.Enqueue(db, new LikeNotification(id, uid));
            await discovery.MarkProfileDirtyAsync(db, uid, now, ct);
        }

        if (active)
            await discovery.MarkSeenAsync(db, uid, id, now, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (changed > 0 && active && author != uid)
            jobs.Wake();
        if (changed > 0)
            await realtime.ChangedAsync(
                id,
                uid,
                active && author != uid ? "Someone liked your post" : null
            );
        return new PostLikeResult();
    }

    internal static async Task<FollowResult> SetFollowAsync(
        Guid id,
        Guid uid,
        bool active,
        AppDb db,
        CancellationToken ct
    )
    {
        if (id == uid)
            return new FollowResult(SelfFollow: true);
        if (!await db.Users.AnyAsync(u => u.Id == id, ct))
            return new FollowResult(UserMissing: true);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (active)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"Follows\" (\"FollowerId\", \"FolloweeId\", \"CreatedAt\") SELECT {uid}, {id}, {DateTime.UtcNow} WHERE EXISTS (SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {id}) ON CONFLICT DO NOTHING",
                ct
            );
        else
            await db
                .Follows.Where(f => f.FollowerId == uid && f.FolloweeId == id)
                .ExecuteDeleteAsync(ct);
        await db
            .FeedSnapshots.Where(snapshot => snapshot.UserId == uid && snapshot.Mode == "following")
            .ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return new FollowResult();
    }

    internal static async Task<CommentLikeResult> SetCommentLikeAsync(
        Guid id,
        Guid uid,
        bool active,
        AppDb db,
        CancellationToken ct
    )
    {
        if (!await db.Comments.AnyAsync(c => c.Id == id && c.Post.ArchivedAt == null, ct))
            return new CommentLikeResult(Missing: true);
        if (active)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"CommentLikes\" (\"CommentId\", \"UserId\", \"CreatedAt\") SELECT {id}, {uid}, {DateTime.UtcNow} WHERE EXISTS (SELECT 1 FROM \"Comments\" c JOIN \"Posts\" p ON p.\"Id\" = c.\"PostId\" WHERE c.\"Id\" = {id} AND p.\"ArchivedAt\" IS NULL AND p.\"IsPublished\" = true) ON CONFLICT DO NOTHING",
                ct
            );
        else
            await db
                .CommentLikes.Where(l => l.CommentId == id && l.UserId == uid)
                .ExecuteDeleteAsync(ct);
        return new CommentLikeResult();
    }

    internal static async Task<FeedData> RankedFeedAsync(
        Guid uid,
        AppDb db,
        DiscoveryService discovery,
        string? mode,
        string? cursor,
        CancellationToken ct
    )
    {
        if (mode is not (null or "for-you" or "following"))
            return new FeedData([], null, mode ?? "for-you", "", BadMode: true);
        var feedMode =
            mode == "following" ? DiscoveryFeedMode.FollowingInterests : DiscoveryFeedMode.ForYou;
        const int size = 10;
        try
        {
            var ranked = await discovery.GetPageAsync(
                db,
                uid,
                feedMode,
                cursor == "start" ? null : cursor,
                size,
                ct
            );
            var ids = ranked.Items.Select(x => x.PostId).ToArray();
            var byId = (
                await PostsService.LoadCardsAsync(
                    db,
                    db.Posts.Where(p => ids.Contains(p.Id) && p.ArchivedAt == null),
                    uid,
                    ct
                )
            ).ToDictionary(p => p.Id);
            var items = ranked
                .Items.Where(x => byId.ContainsKey(x.PostId))
                .Select(x => byId[x.PostId] with { Reason = x.Reason })
                .ToList();
            items = await SocialService.AddReposters(db, items, uid, ct);
            return new FeedData(
                items,
                ranked.NextCursor,
                mode ?? "for-you",
                mode == "following" ? "Following interests" : "For you"
            );
        }
        catch (ArgumentException)
        {
            return new FeedData([], null, mode ?? "for-you", "", BadCursor: true);
        }
        catch (KeyNotFoundException)
        {
            return new FeedData([], null, mode ?? "for-you", "", Gone: true);
        }
    }

    internal static async Task<PostStateResult> SetPostState(
        Guid id,
        string action,
        Guid uid,
        bool active,
        AppDb db,
        JobQueue? jobs,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var post = await db.Posts.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (
            post is null
            || ((post.ArchivedAt != null || !post.IsPublished) && post.AuthorId != uid)
        )
            return new PostStateResult(NotFound: true);
        if (action == "archive")
        {
            if (post.AuthorId != uid)
                return new PostStateResult(Forbidden: true);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            DateTime? archivedAt = active ? DateTime.UtcNow : null;
            var changed = await db
                .Posts.IgnoreQueryFilters()
                .Where(p =>
                    p.Id == id
                    && p.AuthorId == uid
                    && (active ? p.ArchivedAt == null : p.ArchivedAt != null)
                )
                .ExecuteUpdateAsync(
                    s =>
                        s.SetProperty(p => p.ArchivedAt, archivedAt)
                            .SetProperty(p => p.IndexVersion, p => p.IndexVersion + 1),
                    ct
                );
            if (changed > 0)
            {
                var version = await db
                    .Posts.IgnoreQueryFilters()
                    .Where(p => p.Id == id)
                    .Select(p => p.IndexVersion)
                    .SingleAsync(ct);
                await SearchIndexing.RequestAsync(db, jobs!, id, version, ct);
            }

            await tx.CommitAsync(ct);
            jobs!.Wake();
            if (changed > 0)
            {
                if (active)
                    await realtime.RemovedAsync(id, post.AuthorId, post.IsPublished);
                else
                    await realtime.ChangedAsync(id, uid, published: post.IsPublished);
            }
        }
        else if (action == "bookmark")
        {
            if (active)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO \"Bookmarks\" (\"UserId\", \"PostId\", \"CreatedAt\") SELECT {uid}, {id}, {DateTime.UtcNow} WHERE EXISTS (SELECT 1 FROM \"Posts\" WHERE \"Id\" = {id} AND ((\"ArchivedAt\" IS NULL AND \"IsPublished\" = true) OR \"AuthorId\" = {uid})) ON CONFLICT DO NOTHING",
                    ct
                );
            else
                await db
                    .Bookmarks.Where(b => b.UserId == uid && b.PostId == id)
                    .ExecuteDeleteAsync(ct);
        }
        else
        {
            if (post.ArchivedAt != null || !post.IsPublished)
                return new PostStateResult(NotFound: true);
            var changed = active
                ? await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO \"Reposts\" (\"Id\", \"UserId\", \"PostId\", \"CreatedAt\") SELECT {Guid.CreateVersion7()}, {uid}, {id}, {DateTime.UtcNow} WHERE EXISTS (SELECT 1 FROM \"Posts\" WHERE \"Id\" = {id} AND \"ArchivedAt\" IS NULL AND \"IsPublished\" = true) ON CONFLICT DO NOTHING",
                    ct
                )
                : await db
                    .Reposts.Where(b => b.UserId == uid && b.PostId == id)
                    .ExecuteDeleteAsync(ct);
            if (changed > 0)
                await realtime.ChangedAsync(id, uid);
        }

        return new PostStateResult();
    }

    internal static async Task<TimelineData> Chronological(
        Guid? uid,
        AppDb db,
        string mode,
        string title,
        string? cursor,
        UserSummary? user,
        CancellationToken ct
    )
    {
        if (!TryCursor(cursor, out var after))
            return new TimelineData([], null, BadCursor: true);
        var query = db.Posts.IgnoreQueryFilters().AsNoTracking();
        query = mode switch
        {
            "archives" => query.Where(p => p.AuthorId == uid && p.ArchivedAt != null),
            "mine" => query.Where(p =>
                p.ArchivedAt == null
                && (p.AuthorId == uid || p.IsPublished)
                && (p.AuthorId == uid || db.Reposts.Any(r => r.UserId == uid && r.PostId == p.Id))
            ),
            "bookmarks" => query.Where(p =>
                p.ArchivedAt == null
                && p.IsPublished
                && db.Bookmarks.Any(b => b.UserId == uid && b.PostId == p.Id)
            ),
            _ => query.Where(p =>
                p.ArchivedAt == null
                && p.IsPublished
                && (
                    p.AuthorId == user!.Id
                    || db.Reposts.Any(r => r.UserId == user.Id && r.PostId == p.Id)
                )
            ),
        };
        var timelineUser = user?.Id ?? uid ?? Guid.Empty;
        var timeline = mode is "mine" or "user"
            ? query.Select(p => new
            {
                p.Id,
                At = db.Reposts.Where(r => r.UserId == timelineUser && r.PostId == p.Id)
                    .Select(r => (DateTime?)r.CreatedAt)
                    .FirstOrDefault() ?? p.CreatedAt,
            })
            : query.Select(p => new { p.Id, At = p.CreatedAt });
        if (after is { } edge)
            timeline = timeline.Where(p =>
                p.At <= edge.At && (p.At < edge.At || (p.At == edge.At && p.Id < edge.Id))
            );
        const int size = 10;
        var rows = await timeline
            .OrderByDescending(p => p.At)
            .ThenByDescending(p => p.Id)
            .Take(size + 1)
            .ToListAsync(ct);
        var hasMore = rows.Count > size;
        rows = rows.Take(size).ToList();
        var ids = rows.Select(p => p.Id).ToArray();
        var byId = (
            await PostsService.LoadCardsAsync(db, query.Where(p => ids.Contains(p.Id)), uid, ct)
        ).ToDictionary(p => p.Id);
        var items = ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        if (mode is "mine" or "user")
        {
            var reposter =
                user
                ?? (await SocialService.UsersAsync(db, [timelineUser], uid, ct)).FirstOrDefault();
            if (reposter is not null)
                items = items
                    .Select(p =>
                        p.Author.Id == reposter.Id
                            ? p
                            : p with
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

        var next = hasMore && rows.Count > 0 ? Cursor(rows[^1].At, rows[^1].Id) : null;
        return new TimelineData(items, next);
    }

    /// <summary>The /users directory query with follower counts and viewer follow state.</summary>
    internal static async Task<DirectoryData> UsersDirectoryAsync(
        Guid? uid,
        AppDb db,
        string? q,
        string? cursor,
        CancellationToken ct
    )
    {
        Guid? after = null;
        if (cursor is not (null or "start"))
        {
            try
            {
                if (!Guid.TryParse(Encoding.UTF8.GetString(Convert.FromBase64String(cursor)), out var id))
                    return new DirectoryData([], null, BadCursor: true);
                after = id;
            }
            catch (FormatException)
            {
                return new DirectoryData([], null, BadCursor: true);
            }
        }

        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(u => u.DisplayName.Contains(q.Trim()));
        if (after is not null)
            query = query.Where(u => u.Id > after);
        var rows = await query.OrderBy(u => u.Id).Take(21).Select(u => u.Id).ToListAsync(ct);
        var ids = rows.Take(20).ToArray();
        var items = await SocialService.UsersAsync(db, ids, uid, ct);
        var next = rows.Count > 20 ? Convert.ToBase64String(Encoding.UTF8.GetBytes(ids[^1].ToString())) : null;
        return new DirectoryData(items, next);
    }

    /// <summary>The /users/{id} profile header, or null when unknown.</summary>
    public static async Task<UserSummary?> ProfileAsync(
        Guid? uid,
        AppDb db,
        Guid id,
        CancellationToken ct
    ) => (await UsersAsync(db, [id], uid, ct)).FirstOrDefault();

    internal static Task<List<UserSummary>> UsersAsync(
        AppDb db,
        Guid[] ids,
        Guid? uid,
        CancellationToken ct
    ) =>
        db
            .Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .OrderBy(u => u.Id)
            .Select(u => new UserSummary(
                u.Id,
                u.DisplayName,
                u.AvatarUrl,
                db.Follows.Any(f => f.FollowerId == uid && f.FolloweeId == u.Id),
                db.Follows.Count(f => f.FolloweeId == u.Id),
                db.Follows.Count(f => f.FollowerId == u.Id),
                uid == u.Id
            ))
            .ToListAsync(ct);

    internal static async Task<List<PostCardDto>> AddReposters(
        AppDb db,
        List<PostCardDto> items,
        Guid uid,
        CancellationToken ct
    )
    {
        var ids = items.Select(p => p.Id).ToArray();
        var rows = await (
            from r in db.Reposts.AsNoTracking()
            join u in db.Users on r.UserId equals u.Id
            where
                ids.Contains(r.PostId)
                && (
                    r.UserId == uid
                    || db.Follows.Any(f => f.FollowerId == uid && f.FolloweeId == r.UserId)
                )
            orderby r.CreatedAt descending
            select new
            {
                r.PostId,
                u.Id,
                u.DisplayName,
                u.AvatarUrl,
            }
        ).ToListAsync(ct);
        var byId = rows.GroupBy(r => r.PostId).ToDictionary(g => g.Key, g => g.First());
        return items
            .Select(p =>
                byId.TryGetValue(p.Id, out var r)
                    ? p with
                    {
                        Reposter = new AuthorDto(r.Id, r.DisplayName, r.AvatarUrl),
                    }
                    : p
            )
            .ToList();
    }
}
