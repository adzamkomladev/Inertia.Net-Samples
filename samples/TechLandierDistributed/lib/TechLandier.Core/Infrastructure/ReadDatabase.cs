using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TechLandier.Data;

namespace TechLandier.Infrastructure;

/// <summary>
/// Creates read-only contexts for lag-tolerant public catalogue/analytics queries. In the
/// distributed deployment this connection uses PgDog's replica role; without DATABASE_READ_URL it
/// falls back to the primary so tests and single-process development keep working.
/// </summary>
public interface IReadDbFactory
{
    AppDb Create();
}

public sealed class ReadOnlyDbFactory : IReadDbFactory
{
    readonly DbContextOptions<AppDb> options;

    public ReadOnlyDbFactory(IConfiguration config, ILogger<ReadOnlyDbFactory> log)
    {
        var primary = config["DATABASE_URL"] ?? config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("DATABASE_URL is required.");
        var readUrl = config["Database:ReadUrl"];
        var connection = string.IsNullOrWhiteSpace(readUrl) ? primary : readUrl;
        if (string.IsNullOrWhiteSpace(readUrl))
            log.LogWarning("DATABASE_READ_URL is not set; read-only queries fall back to the primary connection.");
        options = new DbContextOptionsBuilder<AppDb>().UseNpgsql(DatabaseConnection.Parse(connection)).Options;
    }

    public AppDb Create() => new(options);
}

/// <summary>
/// Reports Degraded when the configured read connection is unavailable or no longer a replica.
/// The read path is optional, so this never makes the whole service unhealthy.
/// </summary>
public sealed class ReadReplicaHealthCheck(IReadDbFactory reads) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = reads.Create();
            await db.Database.OpenConnectionAsync(cancellationToken);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_is_in_recovery()";
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is true
                ? HealthCheckResult.Healthy("Read connection is a PostgreSQL replica.")
                : HealthCheckResult.Degraded("Read connection is the primary, not a replica.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Read replica is unavailable.", ex);
        }
    }
}
