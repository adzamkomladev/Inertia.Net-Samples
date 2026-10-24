using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Tests;

/// <summary>
/// Opt-in PgDog acceptance test. Set PGDOG_TEST_PRIMARY_URL and PGDOG_TEST_READ_URL to run it;
/// otherwise it is a no-op so the normal suite needs no running PgDog.
/// </summary>
public sealed class PgDogReadTests
{
    [Fact]
    public async Task Read_factory_uses_the_replica_role_and_returns_a_recovery_connection()
    {
        var primary = Environment.GetEnvironmentVariable("PGDOG_TEST_PRIMARY_URL");
        var read = Environment.GetEnvironmentVariable("PGDOG_TEST_READ_URL");
        if (string.IsNullOrWhiteSpace(primary) || string.IsNullOrWhiteSpace(read)) return;

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = primary,
            ["Database:ReadUrl"] = read,
        }).Build();
        var factory = new ReadOnlyDbFactory(config, NullLogger<ReadOnlyDbFactory>.Instance);

        await using (var raw = new NpgsqlConnection(read))
        {
            await raw.OpenAsync();
            await using var rawCommand = raw.CreateCommand();
            rawCommand.CommandText = "SELECT pg_is_in_recovery()";
            Assert.True((bool)(await rawCommand.ExecuteScalarAsync())!);
        }

        await using var readDb = factory.Create();
        Assert.Contains("pgdog.role=replica", readDb.Database.GetConnectionString());
        Assert.True(await readDb.Database.SqlQueryRaw<bool>("SELECT pg_is_in_recovery() AS \"Value\"").SingleAsync());
        // A real EF entity query still executes through the replica-role connection.
        var readCount = await readDb.Products.AsNoTracking().CountAsync();

        await using var primaryDb = new AppDb(new DbContextOptionsBuilder<AppDb>()
            .UseNpgsql(DatabaseConnection.Parse(primary)).Options);
        Assert.False(await primaryDb.Database.SqlQueryRaw<bool>("SELECT pg_is_in_recovery() AS \"Value\"").SingleAsync());
        Assert.Equal(await primaryDb.Products.AsNoTracking().CountAsync(), readCount);
    }
}
