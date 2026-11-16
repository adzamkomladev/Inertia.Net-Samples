using System.Security.Claims;
using TechLandier.Infrastructure;
namespace TechLandier.Features;
public static class Management
{
    public static void MapManagement(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireAuthorization("admin");
        admin.MapGet("/settings", async (ManagementService svc, CancellationToken ct) =>
        {
            var settings = await svc.SettingsAsync(ct);
            return Render("admin/Settings", new InertiaProps { ["settings"] = settings, ["timeZones"] = ManagementService.TimeZoneIds(settings.TimeZoneId) });
        }).WithName("admin.settings");
        admin.MapPost("/settings", async (StoreSettingsView input, ManagementService svc, CancellationToken ct) => await svc.SaveSettingsAsync(input, ct)).WithName("admin.settings.update");
        admin.MapGet("/team", async (ManagementService svc, CancellationToken ct) => Render("admin/Team", new InertiaProps { ["members"] = await svc.TeamAsync(ct) })).WithName("admin.team");
        admin.MapPost("/team", async (TeamInput input, ManagementService svc, CancellationToken ct) => await svc.CreateAdminAsync(input, ct)).WithName("admin.team.store");
        admin.MapPost("/team/{id:guid}/remove", async (Guid id, ClaimsPrincipal user, ManagementService svc, CancellationToken ct) => await svc.RemoveAdminAsync(id, user.UserId(), ct)).WithName("admin.team.remove");
    }
}
