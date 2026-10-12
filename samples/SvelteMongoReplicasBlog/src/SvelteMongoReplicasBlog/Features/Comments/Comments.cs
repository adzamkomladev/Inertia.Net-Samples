namespace SvelteMongoReplicasBlog.Features;

public static class Comments
{
    public static void MapComments(this IEndpointRouteBuilder app)
    {
        app.MapGet("/posts/{id:guid}/comments", CommentsService.CommentsPageAsync);
        app.MapPost("/posts/{id:guid}/comments", CommentsService.AddCommentAsync)
            .RequireAuthorization("verified")
            .WithInertiaValidation()
            .RequireRateLimiting("writes");
        app.MapDelete("/comments/{id:guid}", CommentsService.DeleteCommentAsync)
            .RequireAuthorization()
            .RequireRateLimiting("writes");
    }
}
