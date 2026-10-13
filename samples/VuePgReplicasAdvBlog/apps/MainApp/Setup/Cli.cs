using Microsoft.EntityFrameworkCore;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Search;
using VuePgReplicasAdvBlog.Infrastructure;
using VuePgReplicasAdvBlog.MainApp.Data;

namespace VuePgReplicasAdvBlog.MainApp.Setup;

public static class Cli
{
    public static async Task<bool> TryRunAsync(WebApplication app, string[] args)
    {
        var config = app.Configuration;
        _ = config.RequiredAppBaseUrl();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            if (args.Contains("--migrate", StringComparer.Ordinal) || config.GetValue("Database:MigrateOnStartup", true))
                await db.Database.MigrateAsync();
            if (args.Contains("--migrate", StringComparer.Ordinal))
            {
                Console.WriteLine("Database migrations applied; start the replicas normally.");
                return true;
            }
            if (args.Contains("--reindex-search", StringComparer.Ordinal))
            {
                if (!string.Equals(config["SEARCH_DRIVER"], "meilisearch", StringComparison.OrdinalIgnoreCase)
                    || MeilisearchConnectionSettings.FromConfiguration(config) is null)
                    throw new InvalidOperationException("Configure Meilisearch before queueing a full reindex.");
                var queued = await SearchIndexing.RequestFullReindexAsync(db,
                    scope.ServiceProvider.GetRequiredService<JobQueue>(), CancellationToken.None);
                Console.WriteLine(queued ? "Full reindex queued; start Worker to process batches." : "A full reindex is already queued.");
                return true;
            }
            if (args.Contains("--seed-demo", StringComparer.Ordinal))
            {
                var size = DemoSize(config);
                await db.Database.OpenConnectionAsync();
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT pg_try_advisory_lock(61428742)";
                if (await command.ExecuteScalarAsync() is not true)
                    throw new InvalidOperationException("Another demo seed is running against this database.");
                try { await DemoSeeder.RunAsync(db, size); }
                finally
                {
                    command.CommandText = "SELECT pg_advisory_unlock(61428742)";
                    await command.ExecuteScalarAsync();
                    await db.Database.CloseConnectionAsync();
                }
                var jobs = scope.ServiceProvider.GetRequiredService<JobQueue>();
                var profiles = await scope.ServiceProvider.GetRequiredService<VuePgReplicasAdvBlog.Features.Discovery.DiscoveryService>()
                    .RefreshProfilesAsync(db, jobs, CancellationToken.None);
                if (string.Equals(config["SEARCH_DRIVER"], "meilisearch", StringComparison.OrdinalIgnoreCase)
                    && MeilisearchConnectionSettings.FromConfiguration(config) is not null)
                    await SearchIndexing.RequestFullReindexAsync(db, jobs, CancellationToken.None);
                Console.WriteLine($"Queued {profiles} initial profile refreshes; configured search reindex is coalesced. Start Worker to process background work.");
                return true;
            }
        }
        return false;
    }

    internal static DemoSeedSize DemoSize(IConfiguration config)
    {
        var size = new DemoSeedSize(config.GetValue("Seed:Users", 200), config.GetValue("Seed:Posts", 1000));
        if (size.Users < 20 || size.Posts < 40)
            throw new InvalidOperationException("Demo seeding requires Seed:Users >= 20 and Seed:Posts >= 40.");
        return size;
    }
}
