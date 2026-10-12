using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Search;
namespace VuePgReplicasAdvBlog.Infrastructure;

public sealed class SyncSearchPostHandler(AppDb db, IServiceProvider services) : IJobHandler<SyncSearchPost>
{
    public async Task HandleAsync(SyncSearchPost payload, CancellationToken ct)
    {
        if (!string.Equals(services.GetRequiredService<IConfiguration>()["SEARCH_DRIVER"], "meilisearch", StringComparison.OrdinalIgnoreCase)) return;
        var client = services.GetService<MeilisearchTaskClient>();
        var ns = services.GetService<OptionalServiceNamespace>();
        if (client is null || ns is null) return;
        await SearchIndexing.ProcessAsync(db, client, ns, payload.PostId, ct);
    }
}

public sealed class SyncSearchPostsBatchHandler(AppDb db, JobQueue jobs, IServiceProvider services) : IJobHandler<SyncSearchPostsBatch>
{
    public async Task HandleAsync(SyncSearchPostsBatch payload, CancellationToken ct)
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
        await SearchIndexing.ProcessReindexBatchAsync(db, jobs, client, ns, payload.AfterPostId, ct);
    }
}
