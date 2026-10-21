using System.Text;
using Microsoft.EntityFrameworkCore;
using ReactSqliteBlog.Data;
using ReactSqliteBlog.Features.Discovery;
using ReactSqliteBlog.Features.Posts;
using ReactSqliteBlog.Infrastructure;
using ReactSqliteBlog.Jobs;
using static ReactSqliteBlog.Features.Social.SocialHelpers;

namespace ReactSqliteBlog.Features.Social;

public static partial class SocialFeature
{
    public static void MapSocialEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/posts/{id:guid}/like", ["PUT", "DELETE"], SetPostLikeAsync)
            .RequireAuthorization(AuthPolicies.Verified)
            .RequireRateLimiting(RateLimits.Writes);
        app.MapMethods(
                "/posts/{id:guid}/bookmark",
                ["PUT", "DELETE"],
                (
                    Guid id,
                    HttpContext ctx,
                    AppDb db,
                    RealtimeEvents realtime,
                    CancellationToken ct
                ) => SetPostState(id, "bookmark", ctx, db, null, realtime, ct)
            )
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Writes);
        app.MapMethods(
                "/posts/{id:guid}/repost",
                ["PUT", "DELETE"],
                (
                    Guid id,
                    HttpContext ctx,
                    AppDb db,
                    RealtimeEvents realtime,
                    CancellationToken ct
                ) => SetPostState(id, "repost", ctx, db, null, realtime, ct)
            )
            .RequireAuthorization(AuthPolicies.Verified)
            .RequireRateLimiting(RateLimits.Writes);
        app.MapMethods(
                "/posts/{id:guid}/archive",
                ["PUT", "DELETE"],
                (
                    Guid id,
                    HttpContext ctx,
                    AppDb db,
                    JobQueue jobs,
                    RealtimeEvents realtime,
                    CancellationToken ct
                ) => SetPostState(id, "archive", ctx, db, jobs, realtime, ct)
            )
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Writes);
        app.MapMethods("/users/{id:guid}/follow", ["PUT", "DELETE"], SetFollowAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Writes);
        app.MapMethods(
                "/comments/{id:guid}/like",
                ["PUT", "DELETE"],
                SetCommentLikeAsync
            )
            .RequireAuthorization(AuthPolicies.Verified)
            .RequireRateLimiting(RateLimits.Writes);
        app.MapGet(
                "/me/posts",
                (HttpContext ctx, AppDb db, string? cursor, CancellationToken ct) =>
                    Chronological(ctx, db, "mine", "My posts", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/me/archives",
                (HttpContext ctx, AppDb db, string? cursor, CancellationToken ct) =>
                    Chronological(ctx, db, "archives", "Archives", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/bookmarks",
                (HttpContext ctx, AppDb db, string? cursor, CancellationToken ct) =>
                    Chronological(ctx, db, "bookmarks", "Bookmarks", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet("/feed", RankedFeedAsync).RequireAuthorization();
        app.MapGet("/users", UsersDirectoryAsync);
        app.MapGet("/users/{id:guid}", UserProfileAsync);
    }

    internal static async Task<IResult> SetPostLikeAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        DiscoveryService discovery,
        TimeProvider time,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserId()!.Value;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var author = await db
            .Posts.Where(p => p.Id == id)
            .Select(p => (Guid?)p.AuthorId)
            .FirstOrDefaultAsync(ct);
        if (author is null)
            return ErrorPage.NotFound();
        var now = time.GetUtcNow().UtcDateTime;
        var active = HttpMethods.IsPut(ctx.Request.Method);
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
        return Back();
    }

    internal static async Task<IResult> SetFollowAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserId()!.Value;
        if (id == uid)
            return Results.BadRequest("You cannot follow yourself.");
        if (!await db.Users.AnyAsync(u => u.Id == id, ct))
            return Results.NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (HttpMethods.IsPut(ctx.Request.Method))
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
        var uid = ctx.User.UserId()!.Value;
        if (HttpMethods.IsPut(ctx.Request.Method))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"CommentLikes\" (\"CommentId\", \"UserId\", \"CreatedAt\") SELECT {id}, {uid}, {DateTime.UtcNow} WHERE EXISTS (SELECT 1 FROM \"Comments\" c JOIN \"Posts\" p ON p.\"Id\" = c.\"PostId\" WHERE c.\"Id\" = {id} AND p.\"ArchivedAt\" IS NULL AND p.\"IsPublished\" = true) ON CONFLICT DO NOTHING",
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
        DiscoveryService discovery,
        string? mode,
        string? cursor,
        CancellationToken ct
    )
    {
        if (mode is not (null or "for-you" or "following"))
            return Results.BadRequest("Invalid feed mode.");
        var uid = ctx.User.UserId()!.Value;
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
        var uid = ctx.User.UserId()!.Value;
        var post = await db.Posts.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (
            post is null
            || ((post.ArchivedAt != null || !post.IsPublished) && post.AuthorId != uid)
        )
            return Results.NotFound();
        var active = HttpMethods.IsPut(ctx.Request.Method);
        if (action == "archive")
        {
            if (post.AuthorId != uid)
                return ErrorPage.Forbidden();
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
                return Results.NotFound();
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

        return Back();
    }

    internal static async Task<IResult> Chronological(
        HttpContext ctx,
        AppDb db,
        string mode,
        string title,
        string? cursor,
        UserSummary? user,
        CancellationToken ct
    )
    {
        if (!TryCursor(cursor, out var after))
            return Results.BadRequest("Invalid cursor.");
        var uid = ctx.User.UserId();
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
        if (user is not null)
            return Render(
                "Users/Show",
                new InertiaProps
                {
                    ["user"] = user,
                    ["posts"] = Scroll(new CursorPage<PostCardDto>(items, cursor, next), "data"),
                }
            );
        return Feed(items, cursor, next, mode, title);
    }

    /// <summary>The /users directory query with follower counts and viewer follow state.</summary>
    internal static async Task<IResult> UsersDirectoryAsync(
        HttpContext ctx,
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
                    return Results.BadRequest("Invalid cursor.");
                after = id;
            }
            catch (FormatException)
            {
                return Results.BadRequest("Invalid cursor.");
            }
        }

        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(u => u.DisplayName.Contains(q.Trim()));
        if (after is not null)
            query = query.Where(u => u.Id > after);
        var rows = await query.OrderBy(u => u.Id).Take(21).Select(u => u.Id).ToListAsync(ct);
        var ids = rows.Take(20).ToArray();
        var items = await SocialService.UsersAsync(db, ids, ctx.User.UserId(), ct);
        var next = rows.Count > 20 ? Convert.ToBase64String(Encoding.UTF8.GetBytes(ids[^1].ToString())) : null;
        return Render(
            "Users/Index",
            new InertiaProps
            {
                ["users"] = Scroll(new CursorPage<UserSummary>(items, cursor, next), "data"),
                ["filters"] = new { q = q ?? "" },
            }
        );
    }

    /// <summary>The /users/{id} profile timeline.</summary>
    internal static async Task<IResult> UserProfileAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        string? cursor,
        CancellationToken ct
    )
    {
        var user = (await SocialService.UsersAsync(db, [id], ctx.User.UserId(), ct)).FirstOrDefault();
        if (user is null)
            return Results.NotFound();
        return await Chronological(ctx, db, "user", user.Name, cursor, user, ct);
    }
}
