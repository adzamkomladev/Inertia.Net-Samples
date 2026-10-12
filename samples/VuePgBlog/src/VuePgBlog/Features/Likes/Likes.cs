namespace VuePgBlog.Features;

public static class Likes
{
    public static void MapLikes(this IEndpointRouteBuilder app)
    {
        app.MapMethods(
                "/posts/{id:guid}/like",
                ["POST", "PUT", "DELETE"],
                LikesService.SetPostLikeAsync
            )
            .RequireAuthorization("verified")
            .RequireRateLimiting("writes");
    }
}
