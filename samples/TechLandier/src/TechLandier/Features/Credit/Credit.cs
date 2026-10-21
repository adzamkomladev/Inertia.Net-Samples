using TechLandier.Data;
using TechLandier.Infrastructure;
namespace TechLandier.Features;

public static class Credit
{
    public static void MapCredit(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/account/credit").RequireAuthorization("verified");
        account.MapGet("/", AccountAsync);
        account.MapPost("/installments/{id}/pay", PayInstallmentAsync);

        var admin = app.MapGroup("/admin/credit").RequireAuthorization("admin");
        admin.MapGet("/", AdminAsync);
    }

    static IResult Finish(SaveResult r) => r switch
    {
        { NotFound: true } => Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404),
        { Errors: { } errors } => Back().WithErrors(errors),
        { FlashType: { } type, FlashMessage: { } message } => Back().WithFlash("toast", new Toast(type, message)),
        _ => Back(),
    };

    internal static async Task<IResult> AccountAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var (plans, blocked) = await CreditService.GetAccountAsync(ctx.User.UserId(), db, ct);
        return Render("account/Credit", new InertiaProps
        {
            ["plans"] = plans,
            ["blocked"] = blocked,
        });
    }

    internal static async Task<IResult> PayInstallmentAsync(Guid id, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, IHostEnvironment env, CancellationToken ct) =>
        Finish(await CreditService.PayInstallmentAsync(ctx.User.UserId(), id, db, jobs, realtime, env, ct));

    internal static async Task<IResult> AdminAsync(string? status, int? page, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var (plans, statusFilter) = await CreditService.GetAdminPlansAsync(status, page, db, ct);
        return Render("admin/Credit", new InertiaProps
        {
            ["plans"] = plans,
            ["statusFilter"] = statusFilter,
        });
    }
}
