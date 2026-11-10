using Npgsql;
using Runwick.Postgres.Migrations;

namespace CarParker.Infrastructure.Setup;

/// <summary>
/// Applies Runwick's PostgreSQL schema before the host starts and raises the scheduler capability once.
/// Runwick talks directly to its own primary; Mongo remains the app's transactional job outbox.
/// </summary>
public static class RunwickBootstrap
{
    public static async Task PrepareAsync(IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        if (!RunwickSetup.IsEnabled(config)) return;

        var connectionString = RunwickSetup.ConnectionString(config);
        var migration = await RunwickPostgresMigrations.MigrateAsync(connectionString, ct);
        logger.LogInformation("Runwick PostgreSQL store ready: schema revision {SchemaRevision}, phases [{Phases}]",
            migration.SchemaRevision, string.Join(", ", migration.AppliedPhases));

        if (!config.GetValue("Cron:Enabled", true)) return;

        await EnsureApplicationRowAsync(connectionString, ct);
        var changes = await RunwickPostgresMigrations.ActivateCapabilityAsync(
            connectionString, RunwickSetup.ApplicationName, "scheduler", "carparker-replicas-startup", ct);
        if (changes.Any(c => c.Changed))
            logger.LogInformation("Raised Runwick scheduler capability for {Application}", RunwickSetup.ApplicationName);
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
        command.Parameters.AddWithValue("application", RunwickSetup.ApplicationName);
        await command.ExecuteNonQueryAsync(ct);
    }
}
