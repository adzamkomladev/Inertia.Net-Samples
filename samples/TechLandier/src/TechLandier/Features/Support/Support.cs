using System.Security.Claims;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Support
{
    public static void MapSupport(this IEndpointRouteBuilder app)
    {
        // The public guest complaint form lives on /support/policy.
        app.MapPost("/support", async (GuestComplaintInput input, SupportService svc, CancellationToken ct) => await svc.GuestComplaintAsync(input, ct));

        var account = app.MapGroup("/account/support").RequireAuthorization("verified");
        account.MapGet("/", async (ClaimsPrincipal user, SupportService svc, CancellationToken ct) =>
            Render("account/Support", new InertiaProps
            {
                ["tickets"] = await svc.TicketsAsync(user.UserId(), ct),
                ["categories"] = SupportService.Categories,
            }));
        account.MapPost("/", async (TicketInput input, ClaimsPrincipal user, SupportService svc, CancellationToken ct) => await svc.CreateTicketAsync(user.UserId(), input, ct));
        account.MapPost("/{id:guid}/reply", async (Guid id, ReplyInput input, ClaimsPrincipal user, SupportService svc, CancellationToken ct) => await svc.ReplyAsync(user.UserId(), id, input, ct));

        var admin = app.MapGroup("/admin/support").RequireAuthorization("admin");
        admin.MapGet("/", async (string? filter, int? page, ClaimsPrincipal user, SupportService svc, CancellationToken ct) =>
            Render("admin/Support", new InertiaProps
            {
                ["tickets"] = await svc.AdminTicketsAsync(filter, page, user.UserId(), ct),
                ["filter"] = filter ?? "all",
            }));
        admin.MapGet("/{number}", TicketDetail);
        admin.MapPost("/{id:guid}/reply", async (Guid id, ReplyInput input, ClaimsPrincipal user, SupportService svc, CancellationToken ct) => await svc.AdminReplyAsync(user.UserId(), id, input, ct));
        admin.MapPost("/{id:guid}", async (Guid id, TicketActionInput input, ClaimsPrincipal user, SupportService svc, CancellationToken ct) => await svc.DecideAsync(user.UserId(), id, input, ct));
    }

    static async Task<IResult> TicketDetail(string number, SupportService svc, CancellationToken ct)
    {
        if (await svc.TicketDetailAsync(number, ct) is not { } ticket) return HttpExtensions.NotFoundPage();
        return Render("admin/Support", new InertiaProps { ["ticket"] = ticket, ["filter"] = "all" });
    }
}
