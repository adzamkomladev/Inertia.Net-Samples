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
        if (!app.Environment.IsDevelopment() && config.AppBaseUrl() is null)
            throw new InvalidOperationException("App:BaseUrl must be configured outside Development (set App__BaseUrl to the public URL, e.g. https://blog.example.com).");

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
                await DemoSeeder.RunAsync(db);
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
}
