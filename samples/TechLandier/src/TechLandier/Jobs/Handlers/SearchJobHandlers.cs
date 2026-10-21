using TechLandier.Data;
namespace TechLandier.Infrastructure;
public sealed class SyncSearchProductHandler(AppDb db, IServiceProvider services) : IJobHandler<SyncSearchProduct>
{
    public async Task HandleAsync(SyncSearchProduct payload, CancellationToken ct)
    {
        if (!string.Equals(services.GetRequiredService<IConfiguration>()["SEARCH_DRIVER"], "meilisearch", StringComparison.OrdinalIgnoreCase)) return;
        var client = services.GetService<MeilisearchTaskClient>();
        var ns = services.GetService<OptionalServiceNamespace>();
        if (client is null || ns is null) return;
        await SearchIndexing.ProcessAsync(db, client, ns, payload.ProductId, ct);
    }
}

public sealed class SyncSearchProductsBatchHandler(AppDb db, JobQueue jobs, IServiceProvider services) : IJobHandler<SyncSearchProductsBatch>
{
    public async Task HandleAsync(SyncSearchProductsBatch payload, CancellationToken ct)
    {
        if (!string.Equals(services.GetRequiredService<IConfiguration>()["SEARCH_DRIVER"], "meilisearch", StringComparison.OrdinalIgnoreCase)) return;
        var client = services.GetService<MeilisearchTaskClient>();
        var ns = services.GetService<OptionalServiceNamespace>();
        if (client is null || ns is null) return;
        await SearchIndexing.ProcessBatchAsync(db, jobs, client, ns, ct);
    }
}

public sealed class ReindexSearchBatchHandler(AppDb db, JobQueue jobs, IServiceProvider services) : IJobHandler<ReindexSearchBatch>
{
    public async Task HandleAsync(ReindexSearchBatch payload, CancellationToken ct)
    {
        if (!string.Equals(services.GetRequiredService<IConfiguration>()["SEARCH_DRIVER"], "meilisearch", StringComparison.OrdinalIgnoreCase)) return;
        var client = services.GetService<MeilisearchTaskClient>();
        var ns = services.GetService<OptionalServiceNamespace>();
        if (client is null || ns is null) return;
        await SearchIndexing.ProcessReindexBatchAsync(db, jobs, client, ns, payload.AfterProductId, ct);
    }
}
