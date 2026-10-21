using Inkwell.Data;
using Inkwell.Features.Discovery;
using Inkwell.Jobs;

namespace Inkwell.Infrastructure.Setup;

/// <summary>One-shot command line modes: --reindex-search and --seed-demo.</summary>
public static class Cli
{
    public static bool HasFlag(string[] args, string flag) => args.Contains(flag, StringComparer.Ordinal);

    /// <summary>Runs a CLI mode and returns true when the host should exit without serving requests.</summary>
    public static async Task<bool> TryRunAsync(WebApplication app, string[] args)
    {
        var config = app.Configuration;
        if (HasFlag(args, "--reindex-search"))
        {
            if (!string.Equals(config["SEARCH_DRIVER"], "meilisearch", StringComparison.OrdinalIgnoreCase)
                || MeilisearchConnectionSettings.FromConfiguration(config) is null)
                throw new InvalidOperationException("Configure Meilisearch before queueing a full reindex.");
            await using var scope = app.Services.CreateAsyncScope();
            var queued = await SearchIndexing.RequestFullReindexAsync(scope.ServiceProvider.GetRequiredService<AppDb>(),
                scope.ServiceProvider.GetRequiredService<JobQueue>(), CancellationToken.None);
            Console.WriteLine(queued ? "Full reindex queued; start the application to process batches." : "A full reindex is already queued.");
            return true;
        }

        if (HasFlag(args, "--seed-demo"))
        {
            await using var scope = app.Services.CreateAsyncScope();
            await DemoSeeder.RunAsync(scope.ServiceProvider.GetRequiredService<AppDb>(), CancellationToken.None);
            var jobs = scope.ServiceProvider.GetRequiredService<JobQueue>();
            var profiles = await scope.ServiceProvider.GetRequiredService<DiscoveryService>()
                .RefreshProfilesAsync(scope.ServiceProvider.GetRequiredService<AppDb>(), jobs, CancellationToken.None);
            if (string.Equals(config["SEARCH_DRIVER"], "meilisearch", StringComparison.OrdinalIgnoreCase)
                && MeilisearchConnectionSettings.FromConfiguration(config) is not null)
                await SearchIndexing.RequestFullReindexAsync(scope.ServiceProvider.GetRequiredService<AppDb>(), jobs, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<AppDb>().SaveChangesAsync();
            Console.WriteLine($"Queued {profiles} initial profile refreshes; configured search reindex is coalesced. Start the application to process background work.");
            return true;
        }

        return false;
    }
}
