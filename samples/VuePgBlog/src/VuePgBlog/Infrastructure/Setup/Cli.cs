using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;

namespace VuePgBlog.Infrastructure.Setup;

public static class Cli
{
    public static async Task<bool> TryRunAsync(WebApplication app, string[] args)
    {
        var config = app.Configuration;
        if (!app.Environment.IsDevelopment() && config.AppBaseUrl() is null)
            throw new InvalidOperationException("App:BaseUrl must be configured outside Development (set App__BaseUrl to the public URL, e.g. https://techlandier.example.com).");

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await db.Database.MigrateAsync();
            if (args.Contains("--reindex-search", StringComparer.Ordinal))
            {
                if (!string.Equals(config["SEARCH_DRIVER"], "meilisearch", StringComparison.OrdinalIgnoreCase)
                    || MeilisearchConnectionSettings.FromConfiguration(config) is null)
                    throw new InvalidOperationException("Configure Meilisearch before queueing a full reindex.");
                var queued = await SearchIndexing.RequestFullReindexAsync(db,
                    scope.ServiceProvider.GetRequiredService<JobQueue>(), CancellationToken.None);
                Console.WriteLine(queued ? "Full reindex queued; start the application to process batches." : "A full reindex is already queued.");
                return true;
            }
            if (args.Contains("--seed-demo", StringComparer.Ordinal))
            {
                await DemoSeeder.RunAsync(db);
                if (string.Equals(config["SEARCH_DRIVER"], "meilisearch", StringComparison.OrdinalIgnoreCase)
                    && MeilisearchConnectionSettings.FromConfiguration(config) is not null)
                    await SearchIndexing.RequestFullReindexAsync(db,
                        scope.ServiceProvider.GetRequiredService<JobQueue>(), CancellationToken.None);
                Console.WriteLine("Demo data seeded; configured search reindex is coalesced. Start the application to process background work.");
                return true;
            }
        }
        return false;
    }
}
