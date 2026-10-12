using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Features;
using SvelteMongoReplicasBlog.Features.Discovery;

namespace SvelteMongoReplicasBlog.Infrastructure.Setup;

public static class Cli
{
    public static async Task<bool> TryRunAsync(WebApplication app, string[] args)
    {
        var config = app.Configuration;
        if (!app.Environment.IsDevelopment() && config.AppBaseUrl() is null)
            throw new InvalidOperationException("App:BaseUrl must be configured outside Development (set App__BaseUrl to the public URL, e.g. https://blog.example.com).");

        if (args.Contains("--initialize", StringComparer.Ordinal) || config.GetValue("Database:InitializeOnStartup", true))
            await app.Services.GetRequiredService<AppDb>().InitializeAsync();
        if (args.Contains("--initialize", StringComparer.Ordinal)) return true;
        if (args.Contains("--reindex-search", StringComparer.Ordinal))
        {
            if (!SearchIndexing.IsMeilisearchEnabled(config)) throw new InvalidOperationException("Configure Meilisearch before queueing a full reindex.");
            var queued = await SearchIndexing.RequestFullReindexAsync(app.Services.GetRequiredService<AppDb>(), app.Services.GetRequiredService<JobQueue>());
            Console.WriteLine(queued ? "Full reindex queued; start the application to process batches." : "A full reindex is already queued.");
            return true;
        }
        if (args.Contains("--seed-demo", StringComparer.Ordinal))
        {
            await using var seedScope = app.Services.CreateAsyncScope();
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDb>();
            var seedJobs = seedScope.ServiceProvider.GetRequiredService<JobQueue>();
            await DemoSeeder.RunAsync(seedDb);
            var profiles = await seedScope.ServiceProvider.GetRequiredService<DiscoveryService>().RefreshProfilesAsync(seedDb, seedJobs);
            if (SearchIndexing.IsMeilisearchEnabled(config))
                await SearchIndexing.RequestFullReindexAsync(seedDb, seedJobs);
            Console.WriteLine($"Queued {profiles} initial profile refreshes; configured search reindex is coalesced. Start the application to process background work.");
            return true;
        }
        return false;
    }
}
