using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features;
using CarParker.Features.Lots;
namespace CarParker.Tests;
public sealed class TeamAndHoursTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Staff_creation_and_removal_preserve_accounts_and_revoke_access()
    {
        var (admin, actor) = await app.SeedUserAsync(UserRole.Admin);
        await using var scope = app.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<TeamService>();
        var email = $"staff-{Guid.NewGuid():N}@yopmail.com";
        Assert.NotNull((await svc.CreateAsync(new("New guard", email, "short", "guard"), default)).Errors);
        Assert.Null((await svc.CreateAsync(new("New guard", email, "Password@12345", "guard"), default)).Errors);
        Assert.NotNull((await svc.CreateAsync(new("New guard", email, "Password@12345", "guard"), default)).Errors);
        Assert.NotNull((await svc.RemoveAsync(actor, actor, default)).Errors);
        var (second, id) = await app.SeedUserAsync(UserRole.Admin);
        using var before = await app.InertiaGetAsync(second, "/admin/team");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        await svc.RemoveAsync(id, actor, default);
        using var after = await app.InertiaGetAsync(second, "/admin/team");
        Assert.DoesNotContain("admin/Team", await after.Content.ReadAsStringAsync());
        var role = await app.QueryAsync(async db => await db.Users.Find(u => u.Id == id).Project(u => u.Role).SingleAsync());
        Assert.Equal(UserRole.Customer, role);
    }
    [Fact]
    public void Opening_hours_cover_edges_time_zones_and_overnight_stays()
    {
        var lot = new Lot { Name = "Hours", Address = "Street", Area = "Area", Open24Hours = false, Opens = "09:00", Closes = "17:00", TimeZoneId = "America/New_York" };
        var start = new DateTime(2026, 10, 22, 13, 0, 0, DateTimeKind.Utc);
        Assert.True(LotHours.Contains(lot, start, start.AddHours(8)));
        Assert.False(LotHours.Contains(lot, start.AddMinutes(-1), start.AddHours(1)));
        Assert.False(LotHours.Contains(lot, start, start.AddHours(9)));
        Assert.False(LotHours.Contains(lot, start, start.AddDays(1)));
        Assert.NotNull(LotHours.Error(false, "17:00", "09:00", "UTC"));
        Assert.NotNull(LotHours.Error(false, "09:00", "17:00", "Invalid/Zone"));
        lot.Open24Hours = true;
        Assert.True(LotHours.Contains(lot, start, start.AddDays(2)));
    }
}
