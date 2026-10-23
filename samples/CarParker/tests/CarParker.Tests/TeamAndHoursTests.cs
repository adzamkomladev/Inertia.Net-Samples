using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features;
using CarParker.Features.Lots;
using CarParker.Features.Admin;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
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
    public async Task Customer_creation_search_and_details_keep_roles_and_credentials_private()
    {
        var (admin, _) = await app.SharedUserAsync(UserRole.Admin);
        var (customer, _) = await app.SharedUserAsync();
        var email = $"customer-{Guid.NewGuid():N}@example.test";
        using var created = await app.SendAsync(admin, "/admin/users", new { name = "Searchable Customer", email, password = "Password@12345", role = "admin" });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var account = await app.QueryAsync(async db => await db.Users.Find(u => u.Email == email).SingleAsync());
        Assert.Equal(UserRole.Customer, account.Role);
        Assert.Null(account.EmailVerifiedAt);
        Assert.NotEqual("Password@12345", account.PasswordHash);
        using var found = await app.InertiaGetAsync(admin, $"/admin/users?q={email}&role=customer");
        var json = await found.Content.ReadAsStringAsync();
        Assert.Contains("Searchable Customer", json);
        Assert.DoesNotContain(account.PasswordHash, json);
        using var detail = await app.InertiaGetAsync(admin, $"/admin/users/{account.Id}");
        var body = await detail.Content.ReadAsStringAsync();
        Assert.Contains("admin/User", body);
        Assert.DoesNotContain(account.PasswordHash, body);
        using var denied = await app.InertiaGetAsync(customer, $"/admin/users/{account.Id}");
        Assert.DoesNotContain("admin/User", await denied.Content.ReadAsStringAsync());
        using var duplicate = await app.SendAsync(admin, "/admin/users", new { name = "Duplicate", email, password = "Password@12345" });
        Assert.Equal(HttpStatusCode.Redirect, duplicate.StatusCode);
        Assert.Equal(1, await app.QueryAsync(async db => await db.Users.CountDocumentsAsync(u => u.Email == email)));
        using var missing = await app.InertiaGetAsync(admin, $"/admin/users/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Directory_search_runs_before_limits_and_treats_regex_characters_literally()
    {
        var (_, id) = await app.SharedUserAsync();
        var (lot, spots) = await app.SeedLotAsync("Search [venue]");
        var now = DateTime.UtcNow;
        await app.QueryAsync(async db =>
        {
            await db.Reservations.InsertManyAsync(Enumerable.Range(0, 102).Select(i => new Reservation
            {
                UserId = id, LotId = lot.Id, SpotId = spots[0].Id, PlateNo = i == 0 ? "OLD[42]" : "NEW" + i,
                CreatedAt = now.AddMinutes(i), StartAtUtc = now, EndAtUtc = now.AddHours(1),
            }));
            await db.ParkingSessions.InsertManyAsync(Enumerable.Range(0, 102).Select(i => new ParkingSession
            {
                LotId = lot.Id, SpotId = spots[0].Id, PlateNo = i == 0 ? "OLD[42]" : "NEW" + i,
                OccurrenceId = Guid.NewGuid(), Status = SessionStatus.Exited, EnteredAtUtc = now.AddHours(-1), ExitedAtUtc = now.AddMinutes(i),
            }));
            return true;
        });
        await using var scope = app.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminService>();
        Assert.Single((await svc.BookingsAsync(null, default, "old[42]")).Data);
        Assert.Empty((await svc.BookingsAsync(null, default, ".*")).Data);
        var firstPage = await svc.BookingsAsync(null, default, "[venue]");
        Assert.Equal(25, firstPage.Data.Length);
        Assert.True(firstPage.HasMore);
        Assert.Single((await svc.SessionsAsync(default, "old[42]")).Exited);
        Assert.Empty((await svc.SessionsAsync(default, ".*")).Exited);
        Assert.NotEmpty((await svc.SessionsAsync(default, spots[0].Label)).Exited);
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

    [Fact]
    public async Task Admin_lot_edits_persist_arrival_notes_and_the_public_page_shows_them()
    {
        var (admin, _) = await app.SharedUserAsync(UserRole.Admin);
        var (lot, _) = await app.SeedLotAsync("Arrival notes deck");
        object Input(string? notes) => new
        {
            name = lot.Name, address = lot.Address, area = lot.Area, levelCount = lot.LevelCount,
            ratePerHourCents = lot.RatePerHourCents, dailyMaxCents = lot.DailyMaxCents, isActive = true,
            open24Hours = true, opens = "06:00", closes = "22:00", timeZoneId = "UTC", coverageCents = lot.CoverageCents,
            arrivalNotes = notes,
        };
        using var saved = await app.SendAsync(admin, $"/admin/lots/{lot.Id}", Input("  Enter from the east ramp.  "), HttpMethod.Put);
        Assert.Equal(HttpStatusCode.SeeOther, saved.StatusCode);
        Assert.Equal("Enter from the east ramp.", await app.QueryAsync(async db => await db.Lots.Find(l => l.Id == lot.Id).Project(l => l.ArrivalNotes).SingleAsync()));
        using var page = await app.InertiaGetAsync(admin, $"/lots/{lot.Id}");
        Assert.Contains("Enter from the east ramp.", await page.Content.ReadAsStringAsync());
        using var cleared = await app.SendAsync(admin, $"/admin/lots/{lot.Id}", Input("   "), HttpMethod.Put);
        Assert.Null(await app.QueryAsync(async db => await db.Lots.Find(l => l.Id == lot.Id).Project(l => l.ArrivalNotes).SingleAsync()));
        using var tooLong = await app.SendAsync(admin, $"/admin/lots/{lot.Id}", Input(new string('x', 281)), HttpMethod.Put);
        Assert.Null(await app.QueryAsync(async db => await db.Lots.Find(l => l.Id == lot.Id).Project(l => l.ArrivalNotes).SingleAsync()));
    }

    [Fact]
    public async Task Saved_vehicles_are_normalized_validated_and_prefill_checkout()
    {
        var (customer, id) = await app.SeedUserAsync();
        var (other, otherId) = await app.SeedUserAsync();
        var (lot, _) = await app.SeedLotAsync("Vehicle deck");
        using var saved = await app.SendAsync(customer, "/account/vehicles", new
        {
            vehicles = new object[] { new { plate = " ab-123 ", label = " Daily ", isDefault = false }, new { plate = "xy 9", label = "", isDefault = true } },
        }, HttpMethod.Put);
        Assert.Equal(HttpStatusCode.SeeOther, saved.StatusCode);
        var vehicles = await app.QueryAsync(async db => await db.Users.Find(u => u.Id == id).Project(u => u.Vehicles).SingleAsync());
        Assert.Equal(["AB-123", "XY 9"], vehicles.Select(v => v.Plate));
        Assert.Equal("Daily", vehicles[0].Label);
        Assert.Null(vehicles[1].Label);
        Assert.Equal([false, true], vehicles.Select(v => v.IsDefault));

        // Missing/null lists, null entries, duplicates, bad plates and more than five vehicles are rejected
        // as validation errors (never a 500) without touching the saved list.
        foreach (var bad in new object[]
        {
            new { },
            new { vehicles = (object?)null },
            new { vehicles = new object?[] { null } },
            new { vehicles = new[] { new { plate = "AB-123" }, new { plate = "ab-123" } } },
            new { vehicles = new[] { new { plate = "!!" } } },
            new { vehicles = Enumerable.Range(0, 6).Select(i => new { plate = $"CAR {i}" }).ToArray() },
        })
        {
            using var rejected = await app.SendAsync(customer, "/account/vehicles", bad, HttpMethod.Put);
            Assert.True(rejected.StatusCode is HttpStatusCode.SeeOther or HttpStatusCode.Redirect or HttpStatusCode.Found, $"{rejected.StatusCode} for {System.Text.Json.JsonSerializer.Serialize(bad)}");
        }
        Assert.Equal(2, (await app.QueryAsync(async db => await db.Users.Find(u => u.Id == id).Project(u => u.Vehicles).SingleAsync())).Count);

        using var book = await app.InertiaGetAsync(customer, $"/lots/{lot.Id}/book");
        var json = await book.Content.ReadAsStringAsync();
        Assert.Contains("\"plate\":\"XY 9\"", json);
        using var otherBook = await app.InertiaGetAsync(other, $"/lots/{lot.Id}/book");
        Assert.DoesNotContain("AB-123", await otherBook.Content.ReadAsStringAsync());
        Assert.Empty(await app.QueryAsync(async db => await db.Users.Find(u => u.Id == otherId).Project(u => u.Vehicles).SingleAsync()));
    }
}
