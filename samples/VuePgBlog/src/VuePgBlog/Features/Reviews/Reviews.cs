using VuePgBlog.Data;
using VuePgBlog.Infrastructure;
namespace VuePgBlog.Features;

public static class ReviewsRoutes
{
    public static void MapReviews(this IEndpointRouteBuilder app)
    {
        app.MapPost("/account/reviews", WriteAsync).RequireAuthorization("verified");

        var admin = app.MapGroup("/admin/reviews").RequireAuthorization("admin");
        admin.MapGet("/", AdminAsync);
        admin.MapPost("/{id}", HideAsync);
    }

    static IResult Finish(SaveResult r) => r switch
    {
        { NotFound: true } => Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404),
        { Errors: { } errors } => Back().WithErrors(errors),
        { FlashType: { } type, FlashMessage: { } message } => Back().WithFlash("toast", new Toast(type, message)),
        _ => Back(),
    };

    internal static async Task<IResult> WriteAsync(
        Guid productId, int rating, string? title, string? body, Guid? orderItemId,
        HttpContext ctx, AppDb db, CancellationToken ct) =>
        Finish(await ReviewsService.WriteReviewAsync(productId, rating, title, body, orderItemId, ctx.User.UserId(), db, ct));

    internal static async Task<IResult> AdminAsync(string? state, int? page, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var (reviews, stateName) = await ReviewsService.GetAdminReviewsAsync(state, page, db, ct);
        return Render("admin/Reviews", new InertiaProps
        {
            ["reviews"] = reviews,
            ["state"] = stateName,
        });
    }

    internal static async Task<IResult> HideAsync(Guid id, string? state, HttpContext ctx, AppDb db, CancellationToken ct) =>
        Finish(await ReviewsService.SetReviewStatusAsync(id, state, db, ct));
}
