using System.Security.Claims;
using TechLandier.Infrastructure;
namespace TechLandier.Features;
public static class Management
{
    public static void MapManagement(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireAuthorization("admin");
        admin.MapGet("/settings", async (ManagementService svc, CancellationToken ct) =>
            Render("admin/Settings", new InertiaProps { ["settings"] = await svc.SettingsAsync(ct), ["timeZones"] = TimeZoneInfo.GetSystemTimeZones().Select(z => z.Id).ToList() }));
        admin.MapPost("/settings", async (StoreSettingsView input, ManagementService svc, CancellationToken ct) => await svc.SaveSettingsAsync(input, ct));
        admin.MapGet("/team", async (ManagementService svc, CancellationToken ct) => Render("admin/Team", new InertiaProps { ["members"] = await svc.TeamAsync(ct) }));
        admin.MapPost("/team", async (TeamInput input, ManagementService svc, CancellationToken ct) => await svc.CreateAdminAsync(input, ct));
        admin.MapPost("/team/{id:guid}/remove", async (Guid id, ClaimsPrincipal user, ManagementService svc, CancellationToken ct) => await svc.RemoveAdminAsync(id, user.UserId(), ct));
    }
}
