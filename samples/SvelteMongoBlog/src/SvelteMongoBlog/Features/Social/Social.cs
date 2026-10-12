using SvelteMongoBlog.Data;
using SvelteMongoBlog.Infrastructure;
using static SvelteMongoBlog.Features.SocialService;

namespace SvelteMongoBlog.Features;

public static partial class Social
{
    public static void MapSocial(this IEndpointRouteBuilder app)
    {
        app.MapMethods(
                "/posts/{id:guid}/bookmark",
                ["PUT", "DELETE"],
                SocialService.SetBookmarkAsync
            )
            .RequireAuthorization()
            .RequireRateLimiting("writes");
        app.MapMethods("/posts/{id:guid}/repost", ["PUT", "DELETE"], SocialService.SetRepostAsync)
            .RequireAuthorization("verified")
            .RequireRateLimiting("writes");
        app.MapMethods("/posts/{id:guid}/archive", ["PUT", "DELETE"], SocialService.SetArchiveAsync)
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
                (HttpContext ctx, AppDb db, Storage s, string? cursor, CancellationToken ct) =>
                    Timeline(ctx, db, s, "mine", "My posts", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/me/archives",
                (HttpContext ctx, AppDb db, Storage s, string? cursor, CancellationToken ct) =>
                    Timeline(ctx, db, s, "archives", "Archives", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet(
                "/bookmarks",
                (HttpContext ctx, AppDb db, Storage s, string? cursor, CancellationToken ct) =>
                    Timeline(ctx, db, s, "bookmarks", "Bookmarks", cursor, null, ct)
            )
            .RequireAuthorization();
        app.MapGet("/feed", SocialService.RankedFeedAsync).RequireAuthorization();
        app.MapGet("/users", SocialService.DirectoryAsync);
        app.MapGet("/users/{id:guid}", SocialService.ProfileAsync);
    }
}
