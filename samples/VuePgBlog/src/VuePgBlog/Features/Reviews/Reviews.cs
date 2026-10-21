namespace VuePgBlog.Features;

public static class ReviewsRoutes
{
    public static void MapReviews(this IEndpointRouteBuilder app)
    {
        app.MapPost("/account/reviews", ReviewsService.WriteAsync).RequireAuthorization("verified");

        var admin = app.MapGroup("/admin/reviews").RequireAuthorization("admin");
        admin.MapGet("/", ReviewsService.AdminAsync);
        admin.MapPost("/{id}", ReviewsService.HideAsync);
    }
}
