namespace SvelteMongoReplicasBlog.Features;

public static partial class Posts
{
    public static void MapPosts(this IEndpointRouteBuilder app)
    {
        app.MapGet("/posts", PostsService.FeedAsync);
        app.MapGet("/posts/create", () => Render("Posts/Create")).RequireAuthorization("verified");
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
        app.MapPost("/posts/{id:guid}/view", PostsService.TrackViewAsync)
            .RequireRateLimiting("views");
        app.MapDelete("/posts/{id:guid}", PostsService.DeleteAsync)
            .RequireAuthorization()
            .RequireRateLimiting("writes");
    }
}
