using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechLandier.Data;
using TechLandier.Features;
namespace TechLandier.Tests;

public sealed class ManagementTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Team_creation_validates_duplicates_and_revokes_live_access()
    {
        var (admin, actor) = await app.AdminAsync("team-owner@yopmail.com");
        await using var scope = app.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ManagementService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var invalid = await svc.CreateAdminAsync(new("Staff", "broken", "Password@12345"), default);
        Assert.Contains("email", invalid.Errors!.Keys);
        var created = await svc.CreateAdminAsync(new("Staff", "staff@yopmail.com", "Password@12345"), default);
        Assert.Null(created.Errors);
        var staff = await db.Users.SingleAsync(u => u.Email == "staff@yopmail.com");
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<User>().VerifyHashedPassword(staff, staff.PasswordHash, "Password@12345"));
        Assert.Contains("email", (await svc.CreateAdminAsync(new("Staff", staff.Email, "Password@12345"), default)).Errors!.Keys);
        Assert.Equal("error", (await svc.RemoveAdminAsync(actor, actor, default)).FlashType);
        var (second, secondId) = await app.AdminAsync("team-second@yopmail.com");
        using var before = await app.InertiaGetAsync(second, "/admin/team");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal("success", (await svc.RemoveAdminAsync(secondId, actor, default)).FlashType);
        using var after = await app.InertiaGetAsync(second, "/admin/team");
        Assert.DoesNotContain("admin/Team", await after.Content.ReadAsStringAsync());
        Assert.Equal("customer", await db.Users.Where(u => u.Id == secondId).Select(u => u.Role).SingleAsync());
    }
    [Fact]
    public async Task Settings_roundtrip_and_invalid_hours_leave_saved_values_intact()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ManagementService>();
        var initial = await svc.SettingsAsync(default);
        var changed = initial with { Name = "Updated store", Hours = initial.Hours.Select(h => h with { Opens = "08:30", Closes = "20:00" }).ToList() };
        Assert.Null((await svc.SaveSettingsAsync(changed, default)).Errors);
        Assert.Equal("08:30", (await svc.SettingsAsync(default)).Hours[1].Opens);
        var invalid = changed with { Hours = changed.Hours.Select(h => h with { Closed = false, Opens = "21:00" }).ToList() };
        Assert.NotNull((await svc.SaveSettingsAsync(invalid, default)).Errors);
        Assert.Equal("08:30", (await svc.SettingsAsync(default)).Hours[1].Opens);
        Assert.NotNull(ManagementValidators.SettingsError(changed with { TimeZoneId = "Invalid/Zone" }));
        Assert.NotNull(ManagementValidators.SettingsError(changed with { Hours = [] }));
    }
    [Fact]
    public async Task Customers_cannot_read_or_write_management()
    {
        var (client, _) = await app.SignUpAsync("team-customer@yopmail.com");
        foreach (var path in new[] { "/admin/team", "/admin/settings" })
        {
            using var response = await app.InertiaGetAsync(client, path);
            Assert.DoesNotContain(path == "/admin/team" ? "admin/Team" : "admin/Settings", await response.Content.ReadAsStringAsync());
        }
    }
}
