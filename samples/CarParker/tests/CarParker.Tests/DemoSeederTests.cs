using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using CarParker.Data;

namespace CarParker.Tests;

public class DemoSeederTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Seed_writes_the_full_demo_graph_and_reruns_are_noops()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await DemoSeeder.RunAsync(db);

        Assert.Equal(6, await db.Users.CountDocumentsAsync(u => u.IsDemo));
        Assert.Equal(5, await db.Lots.CountDocumentsAsync(l => l.DemoKey != null));
        Assert.Equal(144, await db.Spots.CountDocumentsAsync(_ => true)); // 12 level sections × 12 spots
        Assert.Equal(30, await db.Reservations.CountDocumentsAsync(_ => true));
        Assert.Equal(30, await db.Payments.CountDocumentsAsync(_ => true));
        Assert.Equal(18, await db.SpotCoverPolicies.CountDocumentsAsync(_ => true));
        Assert.Equal(2, await db.PolicyClaims.CountDocumentsAsync(_ => true));
        Assert.Equal(3, await db.Refunds.CountDocumentsAsync(_ => true));
        Assert.Equal(1, await db.SecurityIncidents.CountDocumentsAsync(_ => true));
        Assert.True(await db.Occurrences.CountDocumentsAsync(_ => true) >= 30);
        Assert.True(await db.ParkingSessions.CountDocumentsAsync(_ => true) >= 4);

        foreach (var phase in new[] { "users", "lots", "spots", "bookings" })
            Assert.Equal(1, (await db.SeedCheckpoints.Find(c => c.Name == $"demo-42-{phase}").SingleAsync()).Completed);

        var admin = await db.Users.Find(u => u.DemoKey == "demo-42-user-admin").SingleAsync();
        Assert.Equal("admin@carparker.test", admin.Email);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.Equal(UserRole.Guard, (await db.Users.Find(u => u.DemoKey == "demo-42-user-guard").SingleAsync()).Role);

        await DemoSeeder.RunAsync(db); // checkpointed phases short-circuit: nothing doubles
        Assert.Equal(6, await db.Users.CountDocumentsAsync(u => u.IsDemo));
        Assert.Equal(30, await db.Reservations.CountDocumentsAsync(_ => true));
        Assert.Equal(admin.PasswordHash, (await db.Users.Find(u => u.Id == admin.Id).SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task Resume_rebuilds_only_the_missing_phase_deterministically()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await DemoSeeder.RunAsync(db);
        var occurrencesBefore = await db.Occurrences.CountDocumentsAsync(_ => true);

        await db.SeedCheckpoints.DeleteOneAsync(c => c.Name == "demo-42-bookings");
        await DemoSeeder.RunAsync(db); // only the bookings phase re-executes, wiping and rebuilding its own rows

        Assert.Equal(1, (await db.SeedCheckpoints.Find(c => c.Name == "demo-42-bookings").SingleAsync()).Completed);
        Assert.Equal(30, await db.Reservations.CountDocumentsAsync(_ => true));
        Assert.Equal(30, await db.Payments.CountDocumentsAsync(_ => true));
        Assert.Equal(occurrencesBefore, await db.Occurrences.CountDocumentsAsync(_ => true));
    }
}
