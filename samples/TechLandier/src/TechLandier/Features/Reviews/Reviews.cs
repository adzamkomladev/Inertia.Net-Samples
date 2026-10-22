using System.Security.Claims;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Reviews
{
    public static void MapReviews(this IEndpointRouteBuilder app)
    {
        app.MapPost("/account/reviews", async (ReviewInput input, ClaimsPrincipal user, ReviewsService svc, CancellationToken ct) =>
            await svc.WriteAsync(user.UserId(), input, ct)).RequireAuthorization("verified");

        var admin = app.MapGroup("/admin/reviews").RequireAuthorization("admin");
        admin.MapGet("/", async (string? state, int? page, ReviewsService svc, CancellationToken ct) =>
            Render("admin/Reviews", new InertiaProps
            {
                ["reviews"] = await svc.AdminReviewsAsync(state, page, ct),
                ["state"] = state == ReviewStatuses.Hidden ? ReviewStatuses.Hidden : ReviewStatuses.Published,
            }));
        admin.MapPost("/{id:guid}", async (Guid id, ReviewStateInput input, ReviewsService svc, CancellationToken ct) => await svc.SetStatusAsync(id, input, ct));
    }
}
