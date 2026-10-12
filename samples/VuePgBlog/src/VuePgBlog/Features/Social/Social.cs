using VuePgBlog.Data;
using VuePgBlog.Infrastructure;
using static VuePgBlog.Features.SocialService;

namespace VuePgBlog.Features;

public static partial class Social
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
        app.MapMethods("/users/{id:guid}/follow", ["PUT", "DELETE"], SocialService.SetFollowAsync)
            .RequireAuthorization()
            .RequireRateLimiting("writes");
        app.MapMethods(
                "/comments/{id:guid}/like",
                ["PUT", "DELETE"],
                SocialService.SetCommentLikeAsync
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
        app.MapGet("/feed", SocialService.RankedFeedAsync).RequireAuthorization();
        app.MapGet("/users", SocialService.DirectoryAsync);
        app.MapGet("/users/{id:guid}", SocialService.ProfileAsync);
    }
}
