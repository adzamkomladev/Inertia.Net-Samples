using TechLandier.Data;
using TechLandier.Infrastructure;
namespace TechLandier.Features;

public static class Support
{
    public static void MapSupport(this IEndpointRouteBuilder app)
    {
        // Public guest complaint form lives on /support/policy.
        app.MapPost("/support", GuestComplaintAsync);

        var account = app.MapGroup("/account/support").RequireAuthorization("verified");
        account.MapGet("/", TicketsAsync);
        account.MapPost("/", CreateTicketAsync);
        account.MapPost("/{id:guid}/reply", ReplyAsync);

        var admin = app.MapGroup("/admin/support").RequireAuthorization("admin");
        admin.MapGet("/", AdminTicketsAsync);
        admin.MapGet("/{number}", TicketDetailAsync);
        admin.MapPost("/{id:guid}/reply", AdminReplyAsync);
        admin.MapPost("/{id:guid}", DecideTicketAsync);
    }

    static IResult Finish(SaveResult r) => r switch
    {
        { NotFound: true } => Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404),
        { Errors: { } errors } => Back().WithErrors(errors),
        { FlashType: { } type, FlashMessage: { } message } => Back().WithFlash("toast", new Toast(type, message)),
        _ => Back(),
    };

    internal static async Task<IResult> TicketsAsync(HttpContext ctx, AppDb db, CancellationToken ct) =>
        Render("account/Support", new InertiaProps
        {
            ["tickets"] = await SupportService.GetTicketsAsync(ctx.User.UserId(), db, ct),
            ["categories"] = SupportService.CategoryList,
        });

    internal static async Task<IResult> CreateTicketAsync(
        string? subject, string? category, string? message, string? orderNumber,
        HttpContext ctx, AppDb db, CancellationToken ct) =>
        Finish(await SupportService.SaveTicketAsync(ctx.User.UserId(), subject, category, message, orderNumber, db, ct));

    internal static async Task<IResult> ReplyAsync(Guid id, string? message, HttpContext ctx, AppDb db, CancellationToken ct) =>
        Finish(await SupportService.ReplyToTicketAsync(ctx.User.UserId(), id, message, db, ct));

    internal static async Task<IResult> GuestComplaintAsync(
        string? name, string? email, string? subject, string? category, string? message, string? orderNumber,
        AppDb db, CancellationToken ct) =>
        Finish(await SupportService.SaveGuestComplaintAsync(name, email, subject, category, message, orderNumber, db, ct));

    internal static async Task<IResult> AdminTicketsAsync(string? filter, int? page, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var (tickets, filterName) = await SupportService.GetAdminTicketsAsync(filter, page, ctx.User.UserId(), db, ct);
        return Render("admin/Support", new InertiaProps
        {
            ["tickets"] = tickets,
            ["filter"] = filterName,
        });
    }

    internal static async Task<IResult> TicketDetailAsync(string number, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var ticket = await SupportService.GetTicketDetailAsync(number, db, ct);
        return ticket is null
            ? Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404)
            : Render("admin/Support", new InertiaProps { ["ticket"] = ticket, ["filter"] = "all" });
    }

    internal static async Task<IResult> AdminReplyAsync(Guid id, string? message, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct) =>
        Finish(await SupportService.SaveAdminReplyAsync(ctx.User.UserIdOrNull(), id, message, db, jobs, realtime, ct));

    internal static async Task<IResult> DecideTicketAsync(Guid id, string action, string? note, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct) =>
        Finish(await SupportService.DecideTicketAsync(ctx.User.UserIdOrNull(), id, action, note, db, jobs, realtime, ct));
}
