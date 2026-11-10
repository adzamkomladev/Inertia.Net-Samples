using Npgsql;
using Runwick.Postgres.Migrations;
using TechLandier.Infrastructure;

namespace TechLandier.Infrastructure.Setup;

/// <summary>
/// The one-shot migrate role prepares Runwick's schema and scheduler capability on the direct primary.
/// App and Worker replicas never migrate Runwick on startup.
/// </summary>
public static class RunwickBootstrap
{
    public static async Task PrepareAsync(IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        if (!RunwickDefaults.IsEnabled(config)) return;

        var connectionString = RunwickDefaults.ConnectionString(config);
        var migration = await RunwickPostgresMigrations.MigrateAsync(connectionString, ct);
        logger.LogInformation("Runwick PostgreSQL store ready: schema revision {SchemaRevision}, phases [{Phases}]",
            migration.SchemaRevision, string.Join(", ", migration.AppliedPhases));

        await EnsureApplicationRowAsync(connectionString, ct);
        var changes = await RunwickPostgresMigrations.ActivateCapabilityAsync(
            connectionString, RunwickDefaults.ApplicationName, "scheduler", "techlandier-distributed-migrate", ct);
        if (changes.Any(c => c.Changed))
            logger.LogInformation("Raised Runwick scheduler capability for {Application}", RunwickDefaults.ApplicationName);
    }

    private static async Task EnsureApplicationRowAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            INSERT INTO runwick.application_controls (application, created_at, updated_at)
            VALUES (@application, now(), now())
            ON CONFLICT (application) DO NOTHING
            """, connection);
        command.Parameters.AddWithValue("application", RunwickDefaults.ApplicationName);
        await command.ExecuteNonQueryAsync(ct);
    }
}
