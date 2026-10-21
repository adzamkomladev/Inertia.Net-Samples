using System.Text;
using Inkwell.Data;
using Inkwell.Features.Discovery;
using Inkwell.Features.Posts;
using Inkwell.Infrastructure;
using Inkwell.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Features.Social;

public sealed record UserSummary(Guid Id, string Name, string? AvatarUrl, bool Following, int FollowerCount, int FollowingCount, bool IsSelf);

public sealed record UserPage(List<UserSummary> Users, string? Next);

public sealed class SocialService(
    AppDb db,
    JobQueue jobs,
    TimeProvider time,
    RealtimeEvents realtime,
    DiscoveryService discovery,
    PostsService posts)
{
    const int PageSize = 10;
    const int UserPageSize = 20;

    DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task<Outcome> SetPostLikeAsync(Guid id, Guid uid, bool active, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var author = await db.Posts.Where(p => p.Id == id).Select(p => (Guid?)p.AuthorId).FirstOrDefaultAsync(ct);
        if (author is null) return Outcome.Missing();
        var now = Now;
        var changed = active
            ? await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "PostLikes" ("PostId", "UserId", "CreatedAt")
                SELECT {id}, {uid}, {now} WHERE EXISTS (SELECT 1 FROM "Posts" WHERE "Id" = {id} AND "ArchivedAt" IS NULL AND "IsPublished" = true)
                ON CONFLICT DO NOTHING
                """, ct)
            : await db.PostLikes.Where(l => l.PostId == id && l.UserId == uid).ExecuteDeleteAsync(ct);
        var notify = changed > 0 && active && author != uid;
        if (changed > 0)
        {
            await db.Posts.Where(p => p.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.LikeCount, p => active ? p.LikeCount + 1 : (p.LikeCount > 0 ? p.LikeCount - 1 : 0)), ct);
            if (notify) jobs.Enqueue(db, new LikeNotification(id, uid));
        }
        if (active) await discovery.MarkSeenAsync(uid, id, now, ct);
        else if (changed > 0) await discovery.MarkProfileDirtyAsync(uid, now, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (notify) jobs.Wake();
        if (changed > 0) await realtime.ChangedAsync(id, uid, notify ? "Someone liked your post" : null);
        return Outcome.Ok();
    }

    public async Task<Outcome> SetCommentLikeAsync(Guid id, Guid uid, bool active, CancellationToken ct)
    {
        // The comment query filter hides comments of archived or unpublished posts.
        if (!await db.Comments.AnyAsync(c => c.Id == id, ct)) return Outcome.Missing();
        if (active)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "CommentLikes" ("CommentId", "UserId", "CreatedAt") VALUES ({id}, {uid}, {Now}) ON CONFLICT DO NOTHING""", ct);
        else
            await db.CommentLikes.Where(l => l.CommentId == id && l.UserId == uid).ExecuteDeleteAsync(ct);
        return Outcome.Ok();
    }

    public async Task<Outcome> SetFollowAsync(Guid id, Guid uid, bool active, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id, ct)) return Outcome.Missing();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (active)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "Follows" ("FollowerId", "FolloweeId", "CreatedAt") VALUES ({uid}, {id}, {Now}) ON CONFLICT DO NOTHING""", ct);
        else
            await db.Follows.Where(f => f.FollowerId == uid && f.FolloweeId == id).ExecuteDeleteAsync(ct);
        await db.FeedSnapshots.Where(s => s.UserId == uid && s.Mode == "following").ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        return Outcome.Ok();
    }

    public async Task<Outcome> SetBookmarkAsync(Guid id, Guid uid, bool active, CancellationToken ct)
    {
        var visible = await db.Posts.IgnoreQueryFilters()
            .AnyAsync(p => p.Id == id && ((p.ArchivedAt == null && p.IsPublished) || p.AuthorId == uid), ct);
        if (!visible) return Outcome.Missing();
        if (active)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "Bookmarks" ("UserId", "PostId", "CreatedAt") VALUES ({uid}, {id}, {Now}) ON CONFLICT DO NOTHING""", ct);
        else
            await db.Bookmarks.Where(b => b.UserId == uid && b.PostId == id).ExecuteDeleteAsync(ct);
        return Outcome.Ok();
    }

    public async Task<Outcome> SetRepostAsync(Guid id, Guid uid, bool active, CancellationToken ct)
    {
        if (!await db.Posts.AnyAsync(p => p.Id == id, ct)) return Outcome.Missing();
        var changed = active
            ? await db.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "Reposts" ("Id", "UserId", "PostId", "CreatedAt") VALUES ({Guid.CreateVersion7()}, {uid}, {id}, {Now}) ON CONFLICT DO NOTHING""", ct)
            : await db.Reposts.Where(r => r.UserId == uid && r.PostId == id).ExecuteDeleteAsync(ct);
        if (changed > 0) await realtime.ChangedAsync(id, uid);
        return Outcome.Ok();
    }

    public async Task<Outcome> SetArchivedAsync(Guid id, Guid uid, bool active, CancellationToken ct)
    {
        var post = await db.Posts.IgnoreQueryFilters().Where(p => p.Id == id)
            .Select(p => new { p.AuthorId, p.IsPublished, p.ArchivedAt })
            .FirstOrDefaultAsync(ct);
        if (post is null || ((post.ArchivedAt != null || !post.IsPublished) && post.AuthorId != uid)) return Outcome.Missing();
        if (post.AuthorId != uid) return Outcome.Denied();
        DateTime? archivedAt = active ? Now : null;
        var changed = await db.Posts.IgnoreQueryFilters()
            .Where(p => p.Id == id && (active ? p.ArchivedAt == null : p.ArchivedAt != null))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ArchivedAt, archivedAt), ct);
        if (changed == 0) return Outcome.Ok();
        if (active) await realtime.RemovedAsync(id, uid, post.IsPublished);
        else await realtime.ChangedAsync(id, uid, published: post.IsPublished);
        return Outcome.Ok();
    }

    /// <summary>The ranked "For you" / "Following" feed. Throws what <see cref="DiscoveryService.GetPageAsync"/> throws for bad or expired cursors.</summary>
    public async Task<(List<PostCardDto> Items, string? NextCursor)> RankedFeedAsync(Guid uid, DiscoveryFeedMode mode, string? cursor, CancellationToken ct)
    {
        var ranked = await discovery.GetPageAsync(uid, mode, cursor == "start" ? null : cursor, PageSize, ct);
        var ids = ranked.Items.Select(i => i.PostId).ToArray();
        var cards = (await posts.LoadCardsAsync(db.Posts.Where(p => ids.Contains(p.Id)), uid, ct)).ToDictionary(c => c.Id);
        var items = ranked.Items.Where(i => cards.ContainsKey(i.PostId)).Select(i => cards[i.PostId] with { Reason = i.Reason }).ToList();
        return (await AddRepostersAsync(items, uid, ct), ranked.NextCursor);
    }

    /// <summary>
    /// A chronological post list: "mine" (own posts and reposts), "archives", "bookmarks" or "user" (a profile's posts
    /// and reposts, which needs <paramref name="profile"/>).
    /// </summary>
    public async Task<CardPage> TimelineAsync(Guid? uid, string mode, Edge? after, UserSummary? profile, CancellationToken ct)
    {
        var all = db.Posts.IgnoreQueryFilters().AsNoTracking();
        var subject = profile?.Id ?? uid ?? Guid.Empty;
        var query = mode switch
        {
            "archives" => all.Where(p => p.AuthorId == uid && p.ArchivedAt != null),
            "mine" => all.Where(p => p.ArchivedAt == null
                && (p.AuthorId == uid || (p.IsPublished && db.Reposts.Any(r => r.UserId == uid && r.PostId == p.Id)))),
            "bookmarks" => all.Where(p => p.ArchivedAt == null && p.IsPublished && db.Bookmarks.Any(b => b.UserId == uid && b.PostId == p.Id)),
            _ => all.Where(p => p.ArchivedAt == null && p.IsPublished
                && (p.AuthorId == subject || db.Reposts.Any(r => r.UserId == subject && r.PostId == p.Id))),
        };
        // Reposts sort at the time they were made, own posts at their creation time.
        var withReposts = mode is "mine" or "user";
        var timeline = withReposts
            ? query.Select(p => new
            {
                p.Id,
                At = db.Reposts.Where(r => r.UserId == subject && r.PostId == p.Id).Select(r => (DateTime?)r.CreatedAt).FirstOrDefault() ?? p.CreatedAt,
            })
            : query.Select(p => new { p.Id, At = p.CreatedAt });
        if (after is { } e)
            timeline = timeline.Where(p => p.At <= e.At && (p.At < e.At || (p.At == e.At && p.Id < e.Id)));
        var rows = await timeline.OrderByDescending(p => p.At).ThenByDescending(p => p.Id).Take(PageSize + 1).ToListAsync(ct);
        var hasMore = rows.Count > PageSize;
        rows = rows.Take(PageSize).ToList();

        var ids = rows.Select(r => r.Id).ToArray();
        var cards = (await posts.LoadCardsAsync(query.Where(p => ids.Contains(p.Id)), uid, ct)).ToDictionary(c => c.Id);
        var items = ids.Where(cards.ContainsKey).Select(id => cards[id]).ToList();
        if (withReposts && (profile ?? (await UsersAsync([subject], uid, ct)).FirstOrDefault()) is { } reposter)
        {
            var by = new AuthorDto(reposter.Id, reposter.Name, reposter.AvatarUrl);
            items = items.Select(p => p.Author.Id == reposter.Id ? p : p with { Reposter = by }).ToList();
        }
        return new CardPage(items, hasMore, hasMore && rows.Count > 0 ? Paging.Encode(rows[^1].At, rows[^1].Id) : null);
    }

    /// <summary>The people directory, keyset-paged by user id; null for a malformed cursor.</summary>
    public async Task<UserPage?> UsersDirectoryAsync(Guid? uid, string? q, string? cursor, CancellationToken ct)
    {
        Guid? after = null;
        if (cursor is not (null or "start"))
        {
            try { after = Guid.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(cursor))); }
            catch (FormatException) { return null; }
        }

        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(u => u.DisplayName.Contains(q.Trim()));
        if (after is not null) query = query.Where(u => u.Id > after);
        var rows = await SummariesOf(query.OrderBy(u => u.Id).Take(UserPageSize + 1), uid).ToListAsync(ct);
        var more = rows.Count > UserPageSize;
        rows = rows.Take(UserPageSize).ToList();
        return new UserPage(rows, more ? Convert.ToBase64String(Encoding.UTF8.GetBytes(rows[^1].Id.ToString())) : null);
    }

    public async Task<UserSummary?> ProfileAsync(Guid? uid, Guid id, CancellationToken ct) =>
        (await UsersAsync([id], uid, ct)).FirstOrDefault();

    Task<List<UserSummary>> UsersAsync(Guid[] ids, Guid? uid, CancellationToken ct) =>
        SummariesOf(db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).OrderBy(u => u.Id), uid).ToListAsync(ct);

    IQueryable<UserSummary> SummariesOf(IQueryable<AppUser> users, Guid? uid) =>
        users.Select(u => new UserSummary(
            u.Id,
            u.DisplayName,
            u.AvatarUrl,
            db.Follows.Any(f => f.FollowerId == uid && f.FolloweeId == u.Id),
            db.Follows.Count(f => f.FolloweeId == u.Id),
            db.Follows.Count(f => f.FollowerId == u.Id),
            uid == u.Id));

    /// <summary>Shows "reposted by" on feed cards for reposts by the viewer or people they follow.</summary>
    async Task<List<PostCardDto>> AddRepostersAsync(List<PostCardDto> items, Guid uid, CancellationToken ct)
    {
        var ids = items.Select(p => p.Id).ToArray();
        var rows = await (
            from r in db.Reposts.AsNoTracking()
            join u in db.Users on r.UserId equals u.Id
            where ids.Contains(r.PostId) && (r.UserId == uid || db.Follows.Any(f => f.FollowerId == uid && f.FolloweeId == r.UserId))
            orderby r.CreatedAt descending
            select new { r.PostId, u.Id, u.DisplayName, u.AvatarUrl }
        ).ToListAsync(ct);
        var latest = rows.GroupBy(r => r.PostId).ToDictionary(g => g.Key, g => g.First());
        return items.Select(p => latest.TryGetValue(p.Id, out var r) ? p with { Reposter = new AuthorDto(r.Id, r.DisplayName, r.AvatarUrl) } : p).ToList();
    }
}
