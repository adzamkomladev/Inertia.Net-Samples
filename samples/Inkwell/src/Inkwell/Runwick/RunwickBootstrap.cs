using Microsoft.Data.Sqlite;
using Runwick;

namespace Inkwell.Infrastructure.Setup;

/// <summary>
/// Applies Runwick's schema to the app's SQLite file and raises the scheduler capability once.
/// Event-driven work stays on Inkwell's own transactional Jobs queue.
/// </summary>
public static class RunwickBootstrap
{
    public static async Task PrepareAsync(IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        if (!RunwickSetup.IsEnabled(config)) return;

        var connectionString = RunwickSetup.ConnectionString(config);
        var migration = await RunwickSqliteMigrations.MigrateAsync(connectionString, ct);
        logger.LogInformation("Runwick SQLite store ready: schema revision {SchemaRevision}, phases [{Phases}]",
            migration.SchemaRevision, string.Join(", ", migration.AppliedPhases));

        if (!config.GetValue("Cron:Enabled", true)) return;

        await EnsureApplicationRowAsync(connectionString, ct);
        var changes = await RunwickSqliteMigrations.ActivateCapabilityAsync(
            connectionString, RunwickSetup.ApplicationName, "scheduler", "inkwell-startup", ct);
        if (changes.Any(c => c.Changed))
            logger.LogInformation("Raised Runwick scheduler capability for {Application}", RunwickSetup.ApplicationName);
    }

    private static async Task EnsureApplicationRowAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO runwick_application_controls (application, created_at, updated_at)
            VALUES (@application, @now, @now)
            ON CONFLICT (application) DO NOTHING
            """;
        command.Parameters.AddWithValue("@application", RunwickSetup.ApplicationName);
        command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await command.ExecuteNonQueryAsync(ct);
    }
}
