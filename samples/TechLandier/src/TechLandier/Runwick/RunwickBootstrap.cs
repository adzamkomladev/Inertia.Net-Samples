using Npgsql;
using Runwick.Postgres.Migrations;
using TechLandier.Data;

namespace TechLandier.Infrastructure.Setup;

/// <summary>
/// Applies Runwick's own schema before the host starts. The scheduler is a one-time capability in Runwick;
/// a sample should not require an operator CLI step, so this bootstraps the application row and raises it
/// through Runwick's public admin API (idempotent, audited).
/// </summary>
public static class RunwickBootstrap
{
    public static async Task PrepareAsync(IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        var connectionString = DatabaseConnection.Parse(config["DATABASE_URL"] ?? config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("DATABASE_URL is required."));
        var migration = await RunwickPostgresMigrations.MigrateAsync(connectionString, ct);
        logger.LogInformation("Runwick store ready: schema revision {SchemaRevision}, phases [{Phases}]",
            migration.SchemaRevision, string.Join(", ", migration.AppliedPhases));

        if (!config.GetValue("Cron:Enabled", true)) return;

        await EnsureApplicationRowAsync(connectionString, ct);
        var changes = await RunwickPostgresMigrations.ActivateCapabilityAsync(
            connectionString, RunwickSetup.ApplicationName, "scheduler", "techlandier-startup", ct);
        if (changes.Any(c => c.Changed))
            logger.LogInformation("Raised Runwick scheduler capability for {Application}", RunwickSetup.ApplicationName);
    }

    /// <summary>RegisterApplication seeds this row on first contact; creating it here lets the scheduler start on a fresh database.</summary>
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
