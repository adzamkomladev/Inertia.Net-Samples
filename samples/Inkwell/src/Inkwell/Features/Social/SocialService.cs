using System.Text;
using Inkwell.Data;
using Inkwell.Features.Discovery;
using Inkwell.Features.Notifications;
using Inkwell.Features.Posts;
using Inkwell.Infrastructure;
using Inkwell.Jobs;
using Inertia.Net;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Features.Social;

public sealed class SocialService(
    AppDb db,
    IDbContextFactory<AppDb> dbs,
    JobQueue jobs,
    TimeProvider time,
    RealtimeEvents realtime,
    DiscoveryService discovery,
    PostsService posts,
    IInertiaCache cache)
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
            if (notify)
            {
                jobs.Enqueue(db, new LikeNotification(id, uid));
                db.Notifications.Add(new Inkwell.Features.Notifications.Notification { UserId = author.Value, Title = "Someone liked your post", Href = $"/posts/{id}", At = now });
            }
        }
        if (active) await discovery.MarkSeenAsync(uid, id, now, ct);
        else if (changed > 0) await discovery.MarkProfileDirtyAsync(uid, now, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (notify) await NotificationsCache.InvalidateAsync(cache, author.Value, ct);
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
                $"""INSERT INTO "CommentLikes" ("CommentId", "UserId", "CreatedAt") SELECT {id}, {uid}, {Now} WHERE EXISTS (SELECT 1 FROM "Comments" c JOIN "Posts" p ON p."Id" = c."PostId" WHERE c."Id" = {id} AND p."ArchivedAt" IS NULL AND p."IsPublished" = true) ON CONFLICT DO NOTHING""", ct);
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
                $"""INSERT INTO "Follows" ("FollowerId", "FolloweeId", "CreatedAt") SELECT {uid}, {id}, {Now} WHERE EXISTS (SELECT 1 FROM "AspNetUsers" WHERE "Id" = {id}) ON CONFLICT DO NOTHING""", ct);
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
                $"""INSERT INTO "Bookmarks" ("UserId", "PostId", "CreatedAt") SELECT {uid}, {id}, {Now} WHERE EXISTS (SELECT 1 FROM "Posts" WHERE "Id" = {id} AND (("ArchivedAt" IS NULL AND "IsPublished" = true) OR "AuthorId" = {uid})) ON CONFLICT DO NOTHING""", ct);
        else
            await db.Bookmarks.Where(b => b.UserId == uid && b.PostId == id).ExecuteDeleteAsync(ct);
        return Outcome.Ok();
    }

    public async Task<Outcome> SetRepostAsync(Guid id, Guid uid, bool active, CancellationToken ct)
    {
        if (!await db.Posts.AnyAsync(p => p.Id == id, ct)) return Outcome.Missing();
        var changed = active
            ? await db.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "Reposts" ("Id", "UserId", "PostId", "CreatedAt") SELECT {Guid.CreateVersion7()}, {uid}, {id}, {Now} WHERE EXISTS (SELECT 1 FROM "Posts" WHERE "Id" = {id} AND "ArchivedAt" IS NULL AND "IsPublished" = true) ON CONFLICT DO NOTHING""", ct)
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
        // Cards and reposter attribution only need the snapshot ids, so they run together on separate contexts.
        await using var cardsDb = await dbs.CreateDbContextAsync(ct);
        var cardsTask = posts.LoadCardsAsync(cardsDb, cardsDb.Posts.Where(p => ids.Contains(p.Id)), uid, ct);
        var repostersTask = RepostersAsync(ids, uid, ct);
        await Task.WhenAll(cardsTask, repostersTask);
        var cards = (await cardsTask).ToDictionary(c => c.Id);
        var items = ranked.Items.Where(i => cards.ContainsKey(i.PostId)).Select(i => cards[i.PostId] with { Reason = i.Reason }).ToList();
        return (ApplyReposters(items, await repostersTask), ranked.NextCursor);
    }

    /// <summary>
    /// A chronological post list: "mine" (own posts and reposts), "archives", "bookmarks" or "user" (a profile's posts
    /// and reposts). <paramref name="subjectId"/> lets a profile route start the timeline before its profile query returns.
    /// </summary>
    public async Task<CardPage> TimelineAsync(Guid? uid, string mode, Edge? after, UserSummary? profile, CancellationToken ct, Guid? subjectId = null)
    {
        var subject = profile?.Id ?? subjectId ?? uid ?? Guid.Empty;
        await using var timelineDb = await dbs.CreateDbContextAsync(ct);
        var query = TimelineQuery(timelineDb, uid, mode, subject);
        // Reposts sort at the time they were made, own posts at their creation time.
        var withReposts = mode is "mine" or "user";
        var timeline = withReposts
            ? query.Select(p => new
            {
                p.Id,
                At = timelineDb.Reposts.Where(r => r.UserId == subject && r.PostId == p.Id).Select(r => (DateTime?)r.CreatedAt).FirstOrDefault() ?? p.CreatedAt,
            })
            : query.Select(p => new { p.Id, At = p.CreatedAt });
        if (after is { } e)
            timeline = timeline.Where(p => p.At <= e.At && (p.At < e.At || (p.At == e.At && p.Id < e.Id)));
        var rows = await timeline.OrderByDescending(p => p.At).ThenByDescending(p => p.Id).Take(PageSize + 1).ToListAsync(ct);
        var hasMore = rows.Count > PageSize;
        rows = rows.Take(PageSize).ToList();

        var ids = rows.Select(r => r.Id).ToArray();
        var cards = (await posts.LoadCardsAsync(timelineDb, query.Where(p => ids.Contains(p.Id)), uid, ct)).ToDictionary(c => c.Id);
        var items = ids.Where(cards.ContainsKey).Select(id => cards[id]).ToList();
        if (withReposts && (profile ?? (await UsersAsync(timelineDb, [subject], uid, ct)).FirstOrDefault()) is { } reposter)
        {
            var by = new AuthorDto(reposter.Id, reposter.Name, reposter.AvatarUrl);
            items = items.Select(p => p.Author.Id == reposter.Id ? p : p with { Reposter = by }).ToList();
        }
        return new CardPage(items, hasMore, hasMore && rows.Count > 0 ? Paging.Encode(rows[^1].At, rows[^1].Id) : null);
    }

    static IQueryable<Post> TimelineQuery(AppDb context, Guid? uid, string mode, Guid subject)
    {
        var all = context.Posts.IgnoreQueryFilters().AsNoTracking();
        return mode switch
        {
            "archives" => all.Where(p => p.AuthorId == uid && p.ArchivedAt != null),
            "mine" => all.Where(p => p.ArchivedAt == null
                && (p.AuthorId == uid || (p.IsPublished && context.Reposts.Any(r => r.UserId == uid && r.PostId == p.Id)))),
            "bookmarks" => all.Where(p => p.ArchivedAt == null && p.IsPublished && context.Bookmarks.Any(b => b.UserId == uid && b.PostId == p.Id)),
            _ => all.Where(p => p.ArchivedAt == null && p.IsPublished
                && (p.AuthorId == subject || context.Reposts.Any(r => r.UserId == subject && r.PostId == p.Id))),
        };
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
        var rows = await SummariesOf(db, query.OrderBy(u => u.Id).Take(UserPageSize + 1), uid).ToListAsync(ct);
        var more = rows.Count > UserPageSize;
        rows = rows.Take(UserPageSize).ToList();
        return new UserPage(rows, more ? Convert.ToBase64String(Encoding.UTF8.GetBytes(rows[^1].Id.ToString())) : null);
    }

    public async Task<UserSummary?> ProfileAsync(Guid? uid, Guid id, CancellationToken ct) =>
        (await UsersAsync(db, [id], uid, ct)).FirstOrDefault();

    async Task<List<UserSummary>> UsersAsync(AppDb context, Guid[] ids, Guid? uid, CancellationToken ct) =>
        await SummariesOf(context, context.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).OrderBy(u => u.Id), uid).ToListAsync(ct);

    static IQueryable<UserSummary> SummariesOf(AppDb context, IQueryable<AppUser> users, Guid? uid) =>
        users.Select(u => new UserSummary(
            u.Id,
            u.DisplayName,
            u.AvatarUrl,
            u.Bio,
            u.CreatedAt,
            context.Follows.Any(f => f.FollowerId == uid && f.FolloweeId == u.Id),
            context.Follows.Count(f => f.FolloweeId == u.Id),
            context.Follows.Count(f => f.FollowerId == u.Id),
            uid == u.Id));

    /// <summary>Latest "reposted by" row per post for the viewer or people they follow.</summary>
    async Task<Dictionary<Guid, AuthorDto>> RepostersAsync(Guid[] ids, Guid uid, CancellationToken ct)
    {
        if (ids.Length == 0) return [];
        var rows = await (
            from r in db.Reposts.AsNoTracking()
            join u in db.Users on r.UserId equals u.Id
            where ids.Contains(r.PostId) && (r.UserId == uid || db.Follows.Any(f => f.FollowerId == uid && f.FolloweeId == r.UserId))
            orderby r.CreatedAt descending
            select new { r.PostId, u.Id, u.DisplayName, u.AvatarUrl }
        ).ToListAsync(ct);
        return rows.GroupBy(r => r.PostId)
            .ToDictionary(g => g.Key, g => new AuthorDto(g.First().Id, g.First().DisplayName, g.First().AvatarUrl));
    }

    static List<PostCardDto> ApplyReposters(List<PostCardDto> items, Dictionary<Guid, AuthorDto> latest) =>
        items.Select(p => latest.TryGetValue(p.Id, out var reposter) ? p with { Reposter = reposter } : p).ToList();
}
