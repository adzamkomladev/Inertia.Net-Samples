using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Features.Discovery;
using SvelteMongoReplicasBlog.Infrastructure;
using static SvelteMongoReplicasBlog.Features.SocialHelpers;

namespace SvelteMongoReplicasBlog.Features;

public static partial class Social
{
    public static void MapSocial(this IEndpointRouteBuilder app)
    {
        app.MapMethods(
                "/posts/{id:guid}/bookmark",
                ["PUT", "DELETE"],
                SetBookmarkAsync
            )
            .RequireAuthorization()
            .RequireRateLimiting("writes");
        app.MapMethods("/posts/{id:guid}/repost", ["PUT", "DELETE"], SetRepostAsync)
            .RequireAuthorization("verified")
            .RequireRateLimiting("writes");
        app.MapMethods("/posts/{id:guid}/archive", ["PUT", "DELETE"], SetArchiveAsync)
            .RequireAuthorization()
            .RequireRateLimiting("writes");
        app.MapMethods("/users/{id:guid}/follow", ["PUT", "DELETE"], SetFollowAsync)
            .RequireAuthorization()
            .RequireRateLimiting("writes");
        app.MapMethods(
                "/comments/{id:guid}/like",
                ["PUT", "DELETE"],
                SetCommentLikeAsync
            )
            .RequireAuthorization("verified")
            .RequireRateLimiting("writes");
        app.MapGet(
                "/me/posts",
                (HttpContext ctx, AppDb db, Storage s, string? cursor, CancellationToken ct) =>
                    Timeline(ctx.User.UserIdOrNull(), db, s, "mine", "My posts", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/me/archives",
                (HttpContext ctx, AppDb db, Storage s, string? cursor, CancellationToken ct) =>
                    Timeline(ctx.User.UserIdOrNull(), db, s, "archives", "Archives", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/bookmarks",
                (HttpContext ctx, AppDb db, Storage s, string? cursor, CancellationToken ct) =>
                    Timeline(ctx.User.UserIdOrNull(), db, s, "bookmarks", "Bookmarks", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet("/feed", RankedFeedAsync).RequireAuthorization();
        app.MapGet("/users", DirectoryAsync);
        app.MapGet("/users/{id:guid}", ProfileAsync);
    }

    static async Task<IResult> SetBookmarkAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var result = await SocialService.SetBookmarkAsync(
            id,
            ctx.User.UserId(),
            HttpMethods.IsPut(ctx.Request.Method),
            db,
            ct
        );
        return result.NotFound ? Results.NotFound() : Back();
    }

    static async Task<IResult> SetRepostAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        RealtimeEvents realtime,
        CancellationToken ct
    ) =>
        (
            await SocialService.SetRepostAsync(
                id,
                ctx.User.UserId(),
                HttpMethods.IsPut(ctx.Request.Method),
                db,
                realtime,
                ct
            )
        ).Respond(ctx);

    static async Task<IResult> SetArchiveAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        IConfiguration config,
        RealtimeEvents realtime,
        CancellationToken ct
    )
    {
        var result = await SocialService.SetArchiveAsync(
            id,
            ctx.User.UserId(),
            HttpMethods.IsPut(ctx.Request.Method),
            db,
            jobs,
            config,
            realtime,
            ct
        );
        if (result.NotFound)
            return Results.NotFound();
        if (result.Forbidden)
            return Render("Error", new InertiaProps { ["status"] = 403 }).WithStatusCode(403);
        return Back();
    }

    static async Task<IResult> SetFollowAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var result = await SocialService.SetFollowAsync(
            id,
            ctx.User.UserId(),
            HttpMethods.IsPut(ctx.Request.Method),
            db,
            ct
        );
        if (result.SelfFollow)
            return Results.BadRequest("You cannot follow yourself.");
        if (result.UserMissing)
            return Results.NotFound();
        return Back();
    }

    static async Task<IResult> SetCommentLikeAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var result = await SocialService.SetCommentLikeAsync(
            id,
            ctx.User.UserId(),
            HttpMethods.IsPut(ctx.Request.Method),
            db,
            ct
        );
        return result.NotFound ? Results.NotFound() : Back();
    }

    static async Task<IResult> RankedFeedAsync(
        HttpContext ctx,
        AppDb db,
        Storage s,
        DiscoveryService discovery,
        string? mode,
        string? cursor,
        CancellationToken ct
    )
    {
        var feed = await SocialService.RankedFeedAsync(
            ctx.User.UserId(),
            db,
            s,
            discovery,
            mode,
            cursor,
            ct
        );
        if (feed.BadMode)
            return Results.BadRequest("Invalid feed mode.");
        if (feed.BadCursor)
            return Results.BadRequest("Invalid feed cursor.");
        if (feed.Gone)
            return Results.StatusCode(410);
        return Render(
            "Feed/Index",
            new InertiaProps
            {
                ["posts"] = Scroll(
                    new CursorPage<PostSummary>(feed.Items, cursor, feed.NextCursor),
                    "data"
                ),
                ["mode"] = feed.Mode,
                ["title"] = feed.Title,
                ["filters"] = new { q = "" },
            }
        );
    }

    static async Task<IResult> DirectoryAsync(
        HttpContext ctx,
        AppDb db,
        Storage s,
        string? q,
        string? cursor,
        CancellationToken ct
    )
    {
        var directory = await SocialService.DirectoryAsync(
            ctx.User.UserIdOrNull(),
            db,
            s,
            q,
            cursor,
            ct
        );
        if (directory.BadCursor)
            return Results.BadRequest("Invalid cursor.");
        return Render(
            "Users/Index",
            new InertiaProps
            {
                ["users"] = Scroll(
                    new CursorPage<UserSummary>(directory.Users, cursor, directory.Next),
                    "data"
                ),
                ["filters"] = new { q = q ?? "" },
            }
        );
    }

    static async Task<IResult> ProfileAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        Storage s,
        string? cursor,
        CancellationToken ct
    )
    {
        var user = await SocialService.ProfileAsync(
            id,
            ctx.User.UserIdOrNull(),
            db,
            s,
            cursor,
            ct
        );
        if (user is null)
            return Results.NotFound();
        return await Timeline(
            ctx.User.UserIdOrNull(),
            db,
            s,
            "user",
            user.Name,
            cursor,
            user,
            ct
        );
    }

    static async Task<IResult> Timeline(
        Guid? uid,
        AppDb db,
        Storage s,
        string mode,
        string title,
        string? cursor,
        UserSummary? profile,
        CancellationToken ct
    )
    {
        var timeline = await SocialService.Timeline(
            uid,
            db,
            s,
            mode,
            title,
            cursor,
            profile,
            ct
        );
        if (timeline.BadCursor)
            return Results.BadRequest("Invalid cursor.");
        var page = new CursorPage<PostSummary>(timeline.Items, cursor, timeline.Next);
        if (profile is not null)
            return Render(
                "Users/Show",
                new InertiaProps { ["user"] = profile, ["posts"] = Scroll(page, "data") }
            );
        return Render(
            "Feed/Index",
            new InertiaProps
            {
                ["posts"] = Scroll(page, "data"),
                ["mode"] = mode,
                ["title"] = title,
                ["filters"] = new { q = "" },
            }
        );
    }
}
