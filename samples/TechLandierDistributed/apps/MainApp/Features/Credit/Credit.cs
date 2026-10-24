using System.Security.Claims;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Credit
{
    public static void MapCredit(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/account/credit").RequireAuthorization("verified");
        account.MapGet("/", async (ClaimsPrincipal user, CreditService credit, CancellationToken ct) =>
        {
            var (plans, blocked) = await credit.AccountAsync(user.UserId(), ct);
            return Render("account/Credit", new InertiaProps { ["plans"] = plans, ["blocked"] = blocked });
        });
        account.MapPost("/installments/{id:guid}/pay", async (Guid id, ClaimsPrincipal user, CreditService credit, CancellationToken ct) =>
            await credit.PayInstallmentAsync(user.UserId(), id, ct));

        app.MapGet("/admin/credit/{id:guid}", async (Guid id, CreditService credit, CancellationToken ct) =>
            await credit.AdminDetailAsync(id, ct) is { } detail
                ? Render("admin/Plan", new InertiaProps { ["detail"] = detail })
                : RouteResults.NotFoundPage()).RequireAuthorization("admin");

        app.MapGroup("/admin/credit").RequireAuthorization("admin")
            .MapGet("/", async (string? status, int? page, CreditService credit, CancellationToken ct) =>
                Render("admin/Credit", new InertiaProps
                {
                    ["plans"] = await credit.AdminPlansAsync(status, page, ct),
                    ["statusFilter"] = status ?? "",
                }));
    }
}
