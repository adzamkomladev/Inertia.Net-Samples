using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Features.Discovery;
using VuePgBlog.Infrastructure;
using static VuePgBlog.Features.SocialHelpers;

namespace VuePgBlog.Features;

public static partial class SocialService
{
    internal static async Task<IResult> SetFollowAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserId();
        if (id == uid)
            return Results.BadRequest("You cannot follow yourself.");
        if (!await db.Users.AnyAsync(u => u.Id == id, ct))
            return Results.NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (HttpMethods.IsPut(ctx.Request.Method))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"Follows\" (\"FollowerId\", \"FolloweeId\", \"CreatedAt\") SELECT {uid}, {id}, {DateTime.UtcNow} WHERE EXISTS (SELECT 1 FROM \"Users\" WHERE \"Id\" = {id}) ON CONFLICT DO NOTHING",
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
        return Back();
    }

    internal static async Task<IResult> SetCommentLikeAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        if (!await db.Comments.AnyAsync(c => c.Id == id && c.Post.ArchivedAt == null, ct))
            return Results.NotFound();
        var uid = ctx.User.UserId();
        if (HttpMethods.IsPut(ctx.Request.Method))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"CommentLikes\" (\"CommentId\", \"UserId\", \"CreatedAt\") SELECT {id}, {uid}, {DateTime.UtcNow} WHERE EXISTS (SELECT 1 FROM \"Comments\" c JOIN \"Posts\" p ON p.\"Id\" = c.\"PostId\" WHERE c.\"Id\" = {id} AND p.\"ArchivedAt\" IS NULL) ON CONFLICT DO NOTHING",
                ct
            );
        else
            await db
                .CommentLikes.Where(l => l.CommentId == id && l.UserId == uid)
                .ExecuteDeleteAsync(ct);
        return Back();
    }

    internal static async Task<IResult> RankedFeedAsync(
        HttpContext ctx,
        AppDb db,
        Storage storage,
        DiscoveryService discovery,
        string? mode,
        string? cursor,
        CancellationToken ct
    )
    {
        if (mode is not (null or "for-you" or "following"))
            return Results.BadRequest("Invalid feed mode.");
        var uid = ctx.User.UserId();
        var feedMode =
            mode == "following" ? DiscoveryFeedMode.FollowingInterests : DiscoveryFeedMode.ForYou;
        var size = await PageSize(db, uid, ct);
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
                await PostsService.LoadSummariesAsync(
                    db,
                    storage,
                    db.Posts.Where(p => ids.Contains(p.Id) && p.ArchivedAt == null),
                    uid,
                    ct
                )
            ).ToDictionary(p => p.Id);
            var items = ranked
                .Items.Where(x => byId.ContainsKey(x.PostId))
                .Select(x => byId[x.PostId] with { Reason = x.Reason })
                .ToList();
            items = await AddReposters(db, storage, items, uid, ct);
            return Feed(
                items,
                cursor,
                ranked.NextCursor,
                mode ?? "for-you",
                mode == "following" ? "Following interests" : "For you"
            );
        }
        catch (ArgumentException)
        {
            return Results.BadRequest("Invalid feed cursor.");
        }
        catch (KeyNotFoundException)
        {
            return Results.StatusCode(410);
        }
    }

    internal static async Task<IResult> DirectoryAsync(
        HttpContext ctx,
        AppDb db,
        Storage storage,
        string? q,
        string? cursor,
        CancellationToken ct
    )
    {
        if (!TryCursor(cursor, out var after))
            return Results.BadRequest("Invalid cursor.");
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(u => u.Name.Contains(q.Trim()));
        if (after is { } edge)
            query = query.Where(u => u.Id > edge.Id);
        var rows = await query
            .OrderBy(u => u.Id)
            .Take(21)
            .Select(u => new
            {
                u.Id,
                u.Name,
                u.AvatarAssetId,
            })
            .ToListAsync(ct);
        var ids = rows.Take(20).Select(u => u.Id).ToArray();
        var items = await UsersAsync(db, storage, ids, ctx.User.UserIdOrNull(), ct);
        var next = rows.Count > 20 ? Cursor(DateTime.UnixEpoch, ids[^1]) : null;
        return Render(
            "Users/Index",
            new InertiaProps
            {
                ["users"] = Scroll(new CursorPage<UserSummary>(items, cursor, next), "data"),
                ["filters"] = new { q = q ?? "" },
            }
        );
    }

    internal static async Task<IResult> ProfileAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        Storage storage,
        string? cursor,
        CancellationToken ct
    )
    {
        var user = (
            await UsersAsync(db, storage, [id], ctx.User.UserIdOrNull(), ct)
        ).FirstOrDefault();
        if (user is null)
            return Results.NotFound();
        return await Chronological(ctx, db, storage, "user", user.Name, cursor, user, ct);
    }

    internal static async Task<IResult> SetPostState(
        Guid id,
        string action,
        HttpContext ctx,
        AppDb db,
        JobQueue? jobs,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserId();
        var post = await db.Posts.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null || (post.ArchivedAt != null && post.AuthorId != uid))
            return Results.NotFound();
        var active = HttpMethods.IsPut(ctx.Request.Method);
        if (action == "archive")
        {
            if (post.AuthorId != uid)
                return HttpExtensions.Forbidden();
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
            if (changed > 0 && active)
                await realtime.RemovedAsync(id, post.AuthorId.ToString(), true);
            else if (changed > 0)
                await realtime.ChangedAsync(id, published: true);
        }
        else if (action == "bookmark")
        {
            if (active)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO \"Bookmarks\" (\"UserId\", \"PostId\", \"CreatedAt\") SELECT {uid}, {id}, {DateTime.UtcNow} WHERE EXISTS (SELECT 1 FROM \"Posts\" WHERE \"Id\" = {id} AND (\"ArchivedAt\" IS NULL OR \"AuthorId\" = {uid})) ON CONFLICT DO NOTHING",
                    ct
                );
            else
                await db
                    .Bookmarks.Where(b => b.UserId == uid && b.PostId == id)
                    .ExecuteDeleteAsync(ct);
        }
        else
        {
            if (post.ArchivedAt != null)
                return Results.NotFound();
            var changed = active
                ? await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO \"Reposts\" (\"Id\", \"UserId\", \"PostId\", \"CreatedAt\") SELECT {Guid.CreateVersion7()}, {uid}, {id}, {DateTime.UtcNow} WHERE EXISTS (SELECT 1 FROM \"Posts\" WHERE \"Id\" = {id} AND \"ArchivedAt\" IS NULL) ON CONFLICT DO NOTHING",
                    ct
                )
                : await db
                    .Reposts.Where(b => b.UserId == uid && b.PostId == id)
                    .ExecuteDeleteAsync(ct);
            if (changed > 0)
                await realtime.ChangedAsync(id);
        }

        return Back();
    }

    internal static async Task<IResult> Chronological(
        HttpContext ctx,
        AppDb db,
        Storage storage,
        string mode,
        string title,
        string? cursor,
        UserSummary? user,
        CancellationToken ct
    )
    {
        if (!TryCursor(cursor, out var after))
            return Results.BadRequest("Invalid cursor.");
        var uid = ctx.User.UserIdOrNull();
        var query = db.Posts.IgnoreQueryFilters().AsNoTracking();
        query = mode switch
        {
            "archives" => query.Where(p => p.AuthorId == uid && p.ArchivedAt != null),
            "mine" => query.Where(p =>
                p.ArchivedAt == null
                && (p.AuthorId == uid || db.Reposts.Any(r => r.UserId == uid && r.PostId == p.Id))
            ),
            "bookmarks" => query.Where(p =>
                p.ArchivedAt == null && db.Bookmarks.Any(b => b.UserId == uid && b.PostId == p.Id)
            ),
            _ => query.Where(p =>
                p.ArchivedAt == null
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
        var size = uid is { } signedIn ? await PageSize(db, signedIn, ct) : 10;
        var rows = await timeline
            .OrderByDescending(p => p.At)
            .ThenByDescending(p => p.Id)
            .Take(size + 1)
            .ToListAsync(ct);
        var hasMore = rows.Count > size;
        rows = rows.Take(size).ToList();
        var ids = rows.Select(p => p.Id).ToArray();
        var byId = (
            await PostsService.LoadSummariesAsync(
                db,
                storage,
                query.Where(p => ids.Contains(p.Id)),
                uid,
                ct
            )
        ).ToDictionary(p => p.Id);
        var items = ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        if (mode is "mine" or "user")
        {
            var reposter =
                user ?? (await UsersAsync(db, storage, [timelineUser], uid, ct)).FirstOrDefault();
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
        if (user is not null)
            return Render(
                "Users/Show",
                new InertiaProps
                {
                    ["user"] = user,
                    ["posts"] = Scroll(new CursorPage<PostSummary>(items, cursor, next), "data"),
                }
            );
        return Feed(items, cursor, next, mode, title);
    }

    internal static async Task<int> PageSize(AppDb db, Guid uid, CancellationToken ct) =>
        Math.Clamp(
            await db
                .Users.Where(u => u.Id == uid)
                .Select(u => u.Preferences.PostsPerPage)
                .FirstAsync(ct),
            5,
            50
        );

    internal static async Task<List<UserSummary>> UsersAsync(
        AppDb db,
        Storage storage,
        Guid[] ids,
        Guid? uid,
        CancellationToken ct
    )
    {
        var rows = await db
            .Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .OrderBy(u => u.Id)
            .Select(u => new
            {
                u.Id,
                u.Name,
                AvatarKey = db
                    .Assets.Where(a => a.Id == u.AvatarAssetId)
                    .Select(a => a.Key)
                    .FirstOrDefault(),
                Following = db.Follows.Any(f => f.FollowerId == uid && f.FolloweeId == u.Id),
                FollowerCount = db.Follows.Count(f => f.FolloweeId == u.Id),
                FollowingCount = db.Follows.Count(f => f.FollowerId == u.Id),
            })
            .ToListAsync(ct);
        return rows.Select(u => new UserSummary(
                u.Id,
                u.Name,
                u.AvatarKey is null ? null : storage.Url(u.AvatarKey),
                u.Following,
                u.FollowerCount,
                u.FollowingCount,
                uid == u.Id
            ))
            .ToList();
    }

    internal static async Task<List<PostSummary>> AddReposters(
        AppDb db,
        Storage storage,
        List<PostSummary> items,
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
                u.Name,
                AvatarKey = db
                    .Assets.Where(a => a.Id == u.AvatarAssetId)
                    .Select(a => a.Key)
                    .FirstOrDefault(),
            }
        ).ToListAsync(ct);
        var byId = rows.GroupBy(r => r.PostId).ToDictionary(g => g.Key, g => g.First());
        return items
            .Select(p =>
                byId.TryGetValue(p.Id, out var r)
                    ? p with
                    {
                        Reposter = new AuthorDto(
                            r.Id,
                            r.Name,
                            r.AvatarKey is null ? null : storage.Url(r.AvatarKey)
                        ),
                    }
                    : p
            )
            .ToList();
    }
}
