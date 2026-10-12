using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Discovery;
using VuePgReplicasAdvBlog.Features.Posts;
using VuePgReplicasAdvBlog.Features.Social;
using VuePgReplicasAdvBlog.Features.Uploads;
using VuePgReplicasAdvBlog.Infrastructure;
using VuePgReplicasAdvBlog.MainApp.Infrastructure;

namespace VuePgReplicasAdvBlog.MainApp.Endpoints;

public static class SocialEndpoints
{
    public static void MapSocial(this IEndpointRouteBuilder app)
    {
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
            .RequireRateLimiting("writes");
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
            .RequireAuthorization("verified")
            .RequireRateLimiting("writes");
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
            .RequireRateLimiting("writes");
        app.MapMethods("/users/{id:guid}/follow", ["PUT", "DELETE"],
            (Guid id, HttpContext ctx, AppDb db, CancellationToken ct) => SetFollowAsync(id, ctx, db, ct))
            .RequireAuthorization()
            .RequireRateLimiting("writes");
        app.MapMethods(
                "/comments/{id:guid}/like",
                ["PUT", "DELETE"],
            (Guid id, HttpContext ctx, AppDb db, CancellationToken ct) => SetCommentLikeAsync(id, ctx, db, ct)
            )
            .RequireAuthorization("verified")
            .RequireRateLimiting("writes");
        app.MapGet(
                "/me/posts",
                (
                    HttpContext ctx,
                    AppDb db,
                    Storage storage,
                    string? cursor,
                    CancellationToken ct
                ) => Chronological(ctx, db, storage, "mine", "My posts", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/me/archives",
                (
                    HttpContext ctx,
                    AppDb db,
                    Storage storage,
                    string? cursor,
                    CancellationToken ct
                ) => Chronological(ctx, db, storage, "archives", "Archives", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/bookmarks",
                (
                    HttpContext ctx,
                    AppDb db,
                    Storage storage,
                    string? cursor,
                    CancellationToken ct
                ) => Chronological(ctx, db, storage, "bookmarks", "Bookmarks", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet("/feed",
            (
                HttpContext ctx,
                AppDb db,
                Storage storage,
                DiscoveryService discovery,
                string? mode,
                string? cursor,
                CancellationToken ct
            ) => RankedFeedAsync(ctx, db, storage, discovery, mode, cursor, ct)).RequireAuthorization();
        app.MapGet("/users",
            (
                HttpContext ctx,
                AppDb db,
                Storage storage,
                string? q,
                string? cursor,
                CancellationToken ct
            ) => DirectoryAsync(ctx, db, storage, q, cursor, ct));
        app.MapGet("/users/{id:guid}",
            (
                Guid id,
                HttpContext ctx,
                AppDb db,
                Storage storage,
                string? cursor,
                CancellationToken ct
            ) => ProfileAsync(id, ctx, db, storage, cursor, ct));
    }

    static async Task<IResult> SetFollowAsync(Guid id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        await SocialService.SetFollowAsync(id, ctx.User.UserId(), HttpMethods.IsPut(ctx.Request.Method), db, ct);
        return Back();
    }

    static async Task<IResult> SetCommentLikeAsync(Guid id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        await SocialService.SetCommentLikeAsync(id, ctx.User.UserId(), HttpMethods.IsPut(ctx.Request.Method), db, ct);
        return Back();
    }

    static async Task<IResult> SetPostState(Guid id, string action, HttpContext ctx, AppDb db, JobQueue? jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        await SocialService.SetPostState(id, action, ctx.User.UserId(), HttpMethods.IsPut(ctx.Request.Method), db, jobs, realtime, ct);
        return Back();
    }

    static async Task<IResult> RankedFeedAsync(HttpContext ctx, AppDb db, Storage storage, DiscoveryService discovery, string? mode, string? cursor, CancellationToken ct) =>
        Feed(await SocialService.RankedFeedAsync(ctx.User.UserId(), db, storage, discovery, mode, cursor, ct), mode ?? "for-you", mode == "following" ? "Following interests" : "For you");

    static async Task<IResult> DirectoryAsync(HttpContext ctx, AppDb db, Storage storage, string? q, string? cursor, CancellationToken ct) =>
        Render("Users/Index", new InertiaProps
        {
            ["users"] = Scroll(CursorScrollPage<UserSummary>.From(await SocialService.DirectoryAsync(ctx.User.UserIdOrNull(), db, storage, q, cursor, ct)), "data"),
            ["filters"] = new { q = q ?? "" }
        });

    static async Task<IResult> ProfileAsync(Guid id, HttpContext ctx, AppDb db, Storage storage, string? cursor, CancellationToken ct)
    {
        var profile = await SocialService.ProfileAsync(id, ctx.User.UserIdOrNull(), db, storage, cursor, ct);
        return Render("Users/Show", new InertiaProps { ["user"] = profile.User, ["posts"] = Scroll(CursorScrollPage<PostSummary>.From(profile.Posts), "data") });
    }

    static async Task<IResult> Chronological(HttpContext ctx, AppDb db, Storage storage, string mode, string title, string? cursor, UserSummary? user, CancellationToken ct) =>
        Feed(await SocialService.Chronological(ctx.User.UserIdOrNull(), db, storage, mode, cursor, user, ct), mode, title);

    static IResult Feed(CursorPage<PostSummary> posts, string mode, string title) =>
        Render("Feed/Index", new InertiaProps
        {
            ["posts"] = Scroll(CursorScrollPage<PostSummary>.From(posts), "data"),
            ["mode"] = mode,
            ["title"] = title,
            ["filters"] = new { q = "" }
        });
}
