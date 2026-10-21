using Microsoft.EntityFrameworkCore;
using Inkwell.Data;
using Inkwell.Features.Discovery;
using Inkwell.Features.Posts;
using Inkwell.Infrastructure;
using Inkwell.Jobs;
using static Inkwell.Features.Social.SocialHelpers;

namespace Inkwell.Features.Social;

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
                ) => SetPostState(
                    id,
                    "bookmark",
                    ctx.User.UserId()!.Value,
                    HttpMethods.IsPut(ctx.Request.Method),
                    db,
                    null,
                    realtime,
                    ct
                )
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
                ) => SetPostState(
                    id,
                    "repost",
                    ctx.User.UserId()!.Value,
                    HttpMethods.IsPut(ctx.Request.Method),
                    db,
                    null,
                    realtime,
                    ct
                )
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
                ) => SetPostState(
                    id,
                    "archive",
                    ctx.User.UserId()!.Value,
                    HttpMethods.IsPut(ctx.Request.Method),
                    db,
                    jobs,
                    realtime,
                    ct
                )
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
                    Timeline(ctx.User.UserId(), db, "mine", "My posts", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/me/archives",
                (HttpContext ctx, AppDb db, string? cursor, CancellationToken ct) =>
                    Timeline(ctx.User.UserId(), db, "archives", "Archives", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/bookmarks",
                (HttpContext ctx, AppDb db, string? cursor, CancellationToken ct) =>
                    Timeline(ctx.User.UserId(), db, "bookmarks", "Bookmarks", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet("/feed", RankedFeedAsync).RequireAuthorization();
        app.MapGet("/users", UsersDirectoryAsync);
        app.MapGet("/users/{id:guid}", UserProfileAsync);
    }

    static async Task<IResult> SetPostLikeAsync(
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
        var result = await SocialService.SetPostLikeAsync(
            id,
            ctx.User.UserId()!.Value,
            HttpMethods.IsPut(ctx.Request.Method),
            db,
            jobs,
            discovery,
            time,
            realtime,
            ct
        );
        return result.Missing ? ErrorPage.NotFound() : Back();
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
            ctx.User.UserId()!.Value,
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
            ctx.User.UserId()!.Value,
            HttpMethods.IsPut(ctx.Request.Method),
            db,
            ct
        );
        return result.Missing ? Results.NotFound() : Back();
    }

    static async Task<IResult> SetPostState(
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
        var result = await SocialService.SetPostState(
            id,
            action,
            uid,
            active,
            db,
            jobs,
            realtime,
            ct
        );
        if (result.NotFound)
            return Results.NotFound();
        if (result.Forbidden)
            return ErrorPage.Forbidden();
        return Back();
    }

    static async Task<IResult> RankedFeedAsync(
        HttpContext ctx,
        AppDb db,
        DiscoveryService discovery,
        string? mode,
        string? cursor,
        CancellationToken ct
    )
    {
        var feed = await SocialService.RankedFeedAsync(
            ctx.User.UserId()!.Value,
            db,
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
        return Feed(feed.Items, cursor, feed.NextCursor, feed.Mode, feed.Title);
    }

    static async Task<IResult> UsersDirectoryAsync(
        HttpContext ctx,
        AppDb db,
        string? q,
        string? cursor,
        CancellationToken ct
    )
    {
        var directory = await SocialService.UsersDirectoryAsync(
            ctx.User.UserId(),
            db,
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
                ["users"] = Scroll(new CursorPage<UserSummary>(directory.Users, cursor, directory.Next), "data"),
                ["filters"] = new { q = q ?? "" },
            }
        );
    }

    static async Task<IResult> UserProfileAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        string? cursor,
        CancellationToken ct
    )
    {
        var user = await SocialService.ProfileAsync(ctx.User.UserId(), db, id, ct);
        if (user is null)
            return Results.NotFound();
        return await Timeline(
            ctx.User.UserId(),
            db,
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
        string mode,
        string title,
        string? cursor,
        UserSummary? user,
        CancellationToken ct
    )
    {
        var timeline = await SocialService.Chronological(
            uid,
            db,
            mode,
            title,
            cursor,
            user,
            ct
        );
        if (timeline.BadCursor)
            return Results.BadRequest("Invalid cursor.");
        if (user is not null)
            return Render(
                "Users/Show",
                new InertiaProps
                {
                    ["user"] = user,
                    ["posts"] = Scroll(new CursorPage<PostCardDto>(timeline.Items, cursor, timeline.Next), "data"),
                }
            );
        return Feed(timeline.Items, cursor, timeline.Next, mode, title);
    }
}
