using System.Security.Claims;
using Inkwell.Infrastructure;
namespace Inkwell.Features.Team;
public static class TeamFeature
{
    public static void MapTeam(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin/team").RequireAuthorization("admin");
        admin.MapGet("/", async (TeamService svc, CancellationToken ct) => Render("Admin/Team", new InertiaProps { ["members"] = await svc.MembersAsync(ct) }));
        admin.MapPost("/", async (TeamInput input, TeamService svc, HttpContext ctx, CancellationToken ct) => (await svc.CreateAsync(input, ct)).Respond(ctx));
        admin.MapPost("/{id:guid}/remove", async (Guid id, TeamService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) => (await svc.RemoveAsync(id, user.UserId()!.Value, ct)).Respond(ctx));
    }
}
