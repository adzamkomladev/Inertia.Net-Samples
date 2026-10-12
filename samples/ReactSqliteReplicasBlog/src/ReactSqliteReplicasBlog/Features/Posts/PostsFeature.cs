using ReactSqliteReplicasBlog.Infrastructure;
using static ReactSqliteReplicasBlog.Features.Posts.PostsService;

namespace ReactSqliteReplicasBlog.Features.Posts;

public static partial class PostsFeature
{
    public static IServiceCollection AddPostsFeature(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapPostEndpoints(this IEndpointRouteBuilder app)
    {
        // Registered before /posts/{id:guid}-style routes; "create" is not a guid so there is no clash.
        app.MapGet("/posts", Feed);
        app.MapGet("/posts/{id:guid}", Show);
        app.MapGet("/posts/{id:guid}/comments", CommentPage);
        app.MapPost("/posts/{id:guid}/view", PostsService.TrackViewAsync)
            .RequireRateLimiting(RateLimits.Writes);
        var verified = app.MapGroup("").RequireAuthorization(AuthPolicies.Verified);
        verified.MapGet("/posts/create", () => Render("Posts/Create"));
        verified.MapPost("/posts", CreatePost).WithInertiaValidation();
        verified.MapGet("/posts/{id:guid}/edit", Edit);
        verified.MapPut("/posts/{id:guid}", UpdatePost).WithInertiaValidation();
        verified.MapDelete("/posts/{id:guid}", DeletePost);
        verified.MapPost("/posts/{id:guid}/like", ToggleLike).RequireRateLimiting(RateLimits.Writes);
        verified
            .MapPost("/posts/{id:guid}/comments", AddComment)
            .WithInertiaValidation()
            .RequireRateLimiting(RateLimits.Writes);
        verified.MapDelete("/comments/{id:guid}", DeleteComment);
        return app;
    }
}
