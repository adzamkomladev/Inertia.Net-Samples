using ReactSqliteReplicasBlog.Data;
using ReactSqliteReplicasBlog.Infrastructure;
using ReactSqliteReplicasBlog.Jobs;
using static ReactSqliteReplicasBlog.Features.Social.SocialService;

namespace ReactSqliteReplicasBlog.Features.Social;

public static partial class SocialFeature
{
    public static void MapSocialEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/posts/{id:guid}/like", ["PUT", "DELETE"], SocialService.SetPostLikeAsync)
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
        app.MapMethods("/users/{id:guid}/follow", ["PUT", "DELETE"], SocialService.SetFollowAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Writes);
        app.MapMethods(
                "/comments/{id:guid}/like",
                ["PUT", "DELETE"],
                SocialService.SetCommentLikeAsync
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
        app.MapGet("/feed", SocialService.RankedFeedAsync).RequireAuthorization();
        app.MapGet("/users", UsersDirectoryAsync);
        app.MapGet("/users/{id:guid}", UserProfileAsync);
    }
}
