namespace VuePgBlog.Features;

public static partial class Posts
{
    public static void MapPosts(this IEndpointRouteBuilder app)
    {
        app.MapGet("/posts", PostsService.FeedAsync);
        app.MapInertia("/posts/create", "Posts/Create").RequireAuthorization("verified");
        app.MapPost("/posts", PostsService.CreateAsync)
            .RequireAuthorization("verified")
            .WithInertiaPrecognition()
            .RequireRateLimiting("writes");
        app.MapGet("/posts/{slug}", PostsService.ShowAsync);
        app.MapGet("/posts/{slug}/edit", PostsService.EditAsync).RequireAuthorization();
        app.MapPut("/posts/{id:guid}", PostsService.UpdateAsync)
            .RequireAuthorization("verified")
            .WithInertiaPrecognition()
            .RequireRateLimiting("writes");
        // View beacon: Show.vue posts here after mount, so cached/prefetched visits (which skip GET /posts/{slug}) still count.
        app.MapPost("/posts/{id:guid}/view", PostsService.TrackViewAsync)
            .RequireRateLimiting("views");
        app.MapDelete("/posts/{id:guid}", PostsService.DeleteAsync)
            .RequireAuthorization()
            .RequireRateLimiting("writes");
    }
}
