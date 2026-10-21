using CarParker.Data;
namespace CarParker.Infrastructure;
public sealed class SyncLotHandler(AppDb db, IServiceProvider services) : IJobHandler<SyncLot>
{
    public async Task HandleAsync(SyncLot payload, CancellationToken ct)
    {
        if (!SearchIndexing.IsMeilisearchEnabled(services.GetRequiredService<IConfiguration>())) return;
        var client = services.GetService<MeilisearchTaskClient>();
        var ns = services.GetService<OptionalServiceNamespace>();
        if (client is null || ns is null) return;
        await SearchIndexing.SyncAsync(db, client, ns, payload.LotId, ct);
    }
}

public sealed class SyncLotsBatchHandler(AppDb db, JobQueue jobs, IServiceProvider services) : IJobHandler<SyncLotsBatch>
{
    public async Task HandleAsync(SyncLotsBatch payload, CancellationToken ct)
    {
        if (!SearchIndexing.IsMeilisearchEnabled(services.GetRequiredService<IConfiguration>())) return;
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
        if (!SearchIndexing.IsMeilisearchEnabled(services.GetRequiredService<IConfiguration>())) return;
        var client = services.GetService<MeilisearchTaskClient>();
        var ns = services.GetService<OptionalServiceNamespace>();
        if (client is null || ns is null) return;
        await SearchIndexing.ProcessReindexBatchAsync(db, jobs, client, ns, payload.AfterLotId, ct);
    }
}
