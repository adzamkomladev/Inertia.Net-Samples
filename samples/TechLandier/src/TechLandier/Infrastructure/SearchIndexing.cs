using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Features;

namespace TechLandier.Infrastructure;

/// <summary>Durable queue item for reconciling one product's search document.</summary>
public sealed record SyncSearchProduct(Guid ProductId) : IJob;
public sealed record SyncSearchProductsBatch : IJob;
public sealed record ReindexSearchBatch(Guid AfterProductId = default) : IJob;

public sealed record SearchProductDocument(
    string Id,
    string Name,
    string Brand,
    string Category,
    string[] Tags,
    string[] Specs,
    decimal PriceMin,
    decimal Rating);

/// <summary>Writes durable index intent before queueing external Meilisearch work.</summary>
public static class SearchIndexing
{
    public const int ReconciliationBatchSize = 500;
    static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    static readonly TimeSpan LaneDuration = TimeSpan.FromMinutes(10);
    const string LaneName = "search-index-lane";

    public static async Task RequestAsync(
        AppDb db,
        JobQueue jobs,
        Guid productId,
        long requestedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(jobs);
        if (productId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(productId));
        if (requestedVersion < 1) throw new ArgumentOutOfRangeException(nameof(requestedVersion));

        // The product mutation must be durable before its search state can be processed. If the caller
        // owns a transaction, both writes remain in that transaction.
        await db.SaveChangesAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "SearchIndexStates" ("ProductId", "RequestedVersion", "AppliedVersion", "TaskId", "LeaseToken", "LockedUntil")
            VALUES ({{productId}}, {{requestedVersion}}, 0, NULL, NULL, NULL)
            ON CONFLICT("ProductId") DO UPDATE SET
                "RequestedVersion" = GREATEST("SearchIndexStates"."RequestedVersion", excluded."RequestedVersion")
            """, cancellationToken);
        await EnqueueBatchAsync(db, jobs, cancellationToken);
    }

    /// <summary>Queues outstanding seeded or recovered index states in bounded batches.</summary>
    public static async Task<int> ReconcileAsync(
        AppDb db,
        JobQueue jobs,
        CancellationToken cancellationToken = default,
        int batchSize = ReconciliationBatchSize)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(jobs);
        if (batchSize is < 1 or > ReconciliationBatchSize) throw new ArgumentOutOfRangeException(nameof(batchSize));

        // Backfill existing products when search is enabled after the database was created.
        var missing = await db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(product => !db.SearchIndexStates.Any(state => state.ProductId == product.Id))
            .OrderBy(product => product.Id).Select(product => new { product.Id, product.IndexVersion })
            .Take(batchSize).ToListAsync(cancellationToken);
        foreach (var product in missing)
            await RequestAsync(db, jobs, product.Id, product.IndexVersion, cancellationToken);

        var now = DateTime.UtcNow;
        if (!await db.SearchIndexStates.AnyAsync(state => state.ProductId != Guid.Empty && state.RequestedVersion > state.AppliedVersion
                && (state.LockedUntil == null || state.LockedUntil < now), cancellationToken)) return missing.Count;
        return missing.Count + (await EnqueueBatchAsync(db, jobs, cancellationToken) ? 1 : 0);
    }

    public static async Task<bool> EnqueueBatchAsync(AppDb db, JobQueue jobs, CancellationToken ct = default)
    {
        if (await db.Jobs.AnyAsync(job => job.Type == nameof(SyncSearchProductsBatch)
                && (job.Status == JobStatus.Pending || job.Status == JobStatus.Running), ct)) return false;
        await jobs.EnqueueAsync(db, new SyncSearchProductsBatch(), ct: ct);
        return true;
    }

    /// <summary>
    /// Processes the latest requested version under a DB lease. The task ID is saved before waiting,
    /// so a restart resumes polling Meilisearch rather than losing the external operation.
    /// </summary>
    public static async Task ProcessAsync(
        AppDb db,
        MeilisearchTaskClient client,
        OptionalServiceNamespace serviceNamespace,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(serviceNamespace);
        if (productId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(productId));

        var token = Guid.NewGuid().ToString("N");
        DateTime now;
        DateTime laneLeaseAt;
        while (true)
        {
            now = DateTime.UtcNow;
            laneLeaseAt = now.AddTicks(Random.Shared.Next(1, 10_000));
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "CronLeases" ("Name", "LastRunAt", "LockedUntil")
                VALUES ({{LaneName}}, NULL, NULL) ON CONFLICT DO NOTHING
                """, cancellationToken);
            var laneClaimed = await db.CronLeases.Where(lease => lease.Name == LaneName
                    && (lease.LockedUntil == null || lease.LockedUntil < now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(lease => lease.LastRunAt, laneLeaseAt)
                    .SetProperty(lease => lease.LockedUntil, now + LaneDuration), cancellationToken);
            if (laneClaimed == 1) break;
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        try
        {
            var claimed = await db.SearchIndexStates.Where(state => state.ProductId == productId
                    && state.RequestedVersion > state.AppliedVersion
                    && (state.LockedUntil == null || state.LockedUntil < now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(state => state.LeaseToken, token)
                    .SetProperty(state => state.LockedUntil, now + LeaseDuration), cancellationToken);
            if (claimed == 0) return;

            var indexUid = serviceNamespace.MeilisearchIndex("products");
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "SearchIndexStates" ("ProductId", "RequestedVersion", "AppliedVersion", "TaskId", "TaskVersion", "LeaseToken", "LockedUntil")
                VALUES ({{Guid.Empty}}, 0, 0, NULL, NULL, NULL, NULL) ON CONFLICT DO NOTHING
                """, cancellationToken);
            await client.EnsureIndexAsync(indexUid, "id", cancellationToken);

            while (true)
            {
                var state = await db.SearchIndexStates.AsNoTracking().SingleAsync(row => row.ProductId == productId, cancellationToken);
                if (state.LeaseToken != token) return;
                if (state.TaskId is { } pendingTask)
                {
                    var indexedVersion = state.TaskVersion;
                    try
                    {
                        await client.WaitForSuccessAsync(new MeilisearchTaskId(pendingTask), cancellationToken: cancellationToken);
                    }
                    catch (MeilisearchTaskFailedException)
                    {
                        await db.SearchIndexStates.Where(row => row.ProductId == productId && row.LeaseToken == token)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TaskId, (long?)null)
                                .SetProperty(row => row.TaskVersion, (long?)null), cancellationToken);
                        throw;
                    }
                    var pendingCompletion = db.SearchIndexStates.Where(row => row.ProductId == productId && row.LeaseToken == token);
                    if (indexedVersion is { } completedVersion)
                        await pendingCompletion.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.AppliedVersion, completedVersion)
                            .SetProperty(row => row.TaskId, (long?)null).SetProperty(row => row.TaskVersion, (long?)null), cancellationToken);
                    else
                        await pendingCompletion.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TaskId, (long?)null)
                            .SetProperty(row => row.TaskVersion, (long?)null), cancellationToken);
                    continue;
                }

                var version = state.RequestedVersion;
                var product = await db.Products.IgnoreQueryFilters().AsNoTracking()
                    .Where(row => row.Id == productId && row.Status == ProductStatuses.Active)
                    .Select(row => new
                    {
                        row.Id,
                        row.Name,
                        row.Brand,
                        row.Summary,
                        row.Keywords,
                        row.SpecsJson,
                        CategoryName = row.Category.Name,
                        PriceMin = Pricing.Display(row.Price, row.DiscountPercent),
                        row.RatingAvg,
                        row.IndexVersion,
                    })
                    .SingleOrDefaultAsync(cancellationToken);

                SearchProductDocument? document = null;
                if (product is not null)
                    document = new SearchProductDocument(
                        product.Id.ToString(),
                        $"{product.Name} {product.Summary}",
                        product.Brand,
                        product.CategoryName,
                        SearchProductTags(product.Keywords),
                        SearchProductSpecs(product.SpecsJson),
                        product.PriceMin,
                        product.RatingAvg);

                var enqueuedTask = document is null
                    ? await client.EnqueueDeleteAsync(indexUid, productId.ToString(), cancellationToken)
                    : await client.EnqueueUpsertAsync(indexUid, new[] { document }, cancellationToken: cancellationToken);
                var saved = await db.SearchIndexStates.Where(row => row.ProductId == productId && row.LeaseToken == token)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TaskId, (long?)enqueuedTask.Value)
                        .SetProperty(row => row.TaskVersion, (long?)version), cancellationToken);
                if (saved == 0) return;

                await client.WaitForSuccessAsync(enqueuedTask, cancellationToken: cancellationToken);
                var completed = await db.SearchIndexStates.Where(row => row.ProductId == productId && row.LeaseToken == token)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(row => row.AppliedVersion, version)
                        .SetProperty(row => row.TaskId, (long?)null)
                        .SetProperty(row => row.TaskVersion, (long?)null), cancellationToken);
                if (completed == 0) return;

                var latest = await db.SearchIndexStates.AsNoTracking().SingleAsync(row => row.ProductId == productId, cancellationToken);
                if (latest.RequestedVersion <= version) return;
            }
        }
        finally
        {
            await db.SearchIndexStates.Where(state => state.ProductId == productId && state.LeaseToken == token)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(state => state.LeaseToken, (string?)null)
                    .SetProperty(state => state.LockedUntil, (DateTime?)null), CancellationToken.None);
            await db.CronLeases.Where(lease => lease.Name == LaneName && lease.LastRunAt == laneLeaseAt)
                .ExecuteUpdateAsync(setters => setters.SetProperty(lease => lease.LockedUntil, (DateTime?)null), CancellationToken.None);
        }
    }

    public static async Task ProcessBatchAsync(AppDb db, JobQueue jobs, MeilisearchTaskClient client,
        OptionalServiceNamespace serviceNamespace, CancellationToken ct = default)
    {
        var lease = Guid.NewGuid().ToString("N");
        var laneLeaseAt = DateTime.UtcNow.AddTicks(Random.Shared.Next(1, 10_000));
        while (true)
        {
            var now = DateTime.UtcNow;
            laneLeaseAt = now.AddTicks(Random.Shared.Next(1, 10_000));
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "CronLeases" ("Name", "LastRunAt", "LockedUntil") VALUES ({{LaneName}}, NULL, NULL)
                ON CONFLICT ("Name") DO NOTHING
                """, ct);
            if (await db.CronLeases.Where(lease => lease.Name == LaneName && (lease.LockedUntil == null || lease.LockedUntil < now))
                    .ExecuteUpdateAsync(set => set.SetProperty(lease => lease.LastRunAt, laneLeaseAt)
                        .SetProperty(lease => lease.LockedUntil, now + LaneDuration), ct) == 1) break;
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }

        try
        {
            var indexUid = serviceNamespace.MeilisearchIndex("products");
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "SearchIndexStates" ("ProductId", "RequestedVersion", "AppliedVersion", "TaskId", "TaskVersion", "LeaseToken", "LockedUntil")
                VALUES ({{Guid.Empty}}, 0, 0, NULL, NULL, NULL, NULL) ON CONFLICT ("ProductId") DO NOTHING
                """, ct);
            await client.EnsureIndexAsync(indexUid, "id", ct);

            var now = DateTime.UtcNow;
            var ids = await db.SearchIndexStates.AsNoTracking()
                .Where(row => row.ProductId != Guid.Empty && row.RequestedVersion > row.AppliedVersion && (row.LockedUntil == null || row.LockedUntil < now))
                .OrderBy(row => row.ProductId).Select(row => row.ProductId).Take(ReconciliationBatchSize).ToListAsync(ct);
            if (ids.Count == 0) return;

            var states = await db.SearchIndexStates.Where(row => ids.Contains(row.ProductId)).ToListAsync(ct);

            // Finish tasks left over from a previous attempt before submitting new work.
            foreach (var taskId in states.Where(row => row.TaskId.HasValue).Select(row => row.TaskId!.Value).Distinct())
            {
                try { await client.WaitForSuccessAsync(new MeilisearchTaskId(taskId), cancellationToken: ct); }
                catch (MeilisearchTaskFailedException)
                {
                    foreach (var state in states.Where(row => row.TaskId == taskId)) { state.TaskId = null; state.TaskVersion = null; }
                    await db.SaveChangesAsync(ct);
                    throw;
                }
                foreach (var state in states.Where(row => row.TaskId == taskId))
                {
                    if (state.TaskVersion is { } completedVersion) state.AppliedVersion = completedVersion;
                    state.TaskId = null;
                    state.TaskVersion = null;
                }
            }

            var claimed = states.Where(row => row.ProductId != Guid.Empty && row.RequestedVersion > row.AppliedVersion
                && (row.LockedUntil == null || row.LockedUntil < now)).ToList();
            if (claimed.Count == 0) { await db.SaveChangesAsync(ct); return; }
            foreach (var state in claimed) { state.LeaseToken = lease; state.LockedUntil = now + LeaseDuration; }
            await db.SaveChangesAsync(ct);

            var claimedIds = claimed.Select(row => row.ProductId).ToArray();
            var products = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(product => claimedIds.Contains(product.Id))
                .Select(product => new
                {
                    product.Id,
                    product.Name,
                    product.Summary,
                    product.Brand,
                    product.Keywords,
                    product.SpecsJson,
                    CategoryName = product.Category.Name,
                    PriceMin = Pricing.Display(product.Price, product.DiscountPercent),
                    product.RatingAvg,
                    product.Status,
                    product.IndexVersion,
                })
                .ToListAsync(ct);

            var upsertProducts = products.Where(product => product.Status == ProductStatuses.Active).ToArray();
            var upsertIds = upsertProducts.Select(product => product.Id).ToArray();

            if (upsertProducts.Length > 0)
            {
                var documents = upsertProducts.Select(product => new SearchProductDocument(
                    product.Id.ToString(),
                    $"{product.Name} {product.Summary}",
                    product.Brand,
                    product.CategoryName,
                    SearchProductTags(product.Keywords),
                    SearchProductSpecs(product.SpecsJson),
                    product.PriceMin,
                    product.RatingAvg)).ToArray();
                await SubmitAsync(upsertIds, documents);
            }

            // Products that no longer exist (deleted) are removed from the index too.
            var missingIds = claimedIds.Except(products.Select(product => product.Id)).ToArray();
            var deleteIds = products.Where(product => product.Status != ProductStatuses.Active).Select(product => product.Id).Concat(missingIds).ToArray();
            if (deleteIds.Length > 0) await SubmitAsync(deleteIds, null);

            foreach (var state in claimed) { state.LeaseToken = null; state.LockedUntil = null; }
            await db.SaveChangesAsync(ct);

            // Drain the next batch promptly while unlocked work remains.
            if (await db.SearchIndexStates.AnyAsync(row => row.ProductId != Guid.Empty && row.RequestedVersion > row.AppliedVersion
                    && (row.LockedUntil == null || row.LockedUntil < DateTime.UtcNow), ct))
            {
                jobs.Enqueue(db, new SyncSearchProductsBatch());
                await db.SaveChangesAsync(ct);
                jobs.Wake();
            }

            async Task SubmitAsync(Guid[] batchIds, SearchProductDocument[]? documents)
            {
                var task = documents is { Length: > 0 }
                    ? await client.EnqueueUpsertAsync(indexUid, documents, cancellationToken: ct)
                    : await client.EnqueueDeleteBatchAsync(indexUid, batchIds.Select(id => id.ToString()).ToArray(), ct);
                var batchStates = claimed.Where(state => batchIds.Contains(state.ProductId)).ToArray();
                foreach (var state in batchStates) { state.TaskId = task.Value; state.TaskVersion = state.RequestedVersion; }
                await db.SaveChangesAsync(ct);
                try { await client.WaitForSuccessAsync(task, cancellationToken: ct); }
                catch (MeilisearchTaskFailedException)
                {
                    foreach (var state in batchStates) { state.TaskId = null; state.TaskVersion = null; }
                    await db.SaveChangesAsync(ct);
                    throw;
                }
                foreach (var state in batchStates)
                {
                    if (state.TaskVersion is { } completedVersion) state.AppliedVersion = completedVersion;
                    state.TaskId = null;
                    state.TaskVersion = null;
                }
            }
        }
        finally
        {
            await db.SearchIndexStates.Where(row => row.LeaseToken == lease)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseToken, (string?)null).SetProperty(row => row.LockedUntil, (DateTime?)null), CancellationToken.None);
            await db.CronLeases.Where(lease => lease.Name == LaneName && lease.LastRunAt == laneLeaseAt)
                .ExecuteUpdateAsync(set => set.SetProperty(lease => lease.LockedUntil, (DateTime?)null), CancellationToken.None);
        }
    }

    public static async Task<bool> RequestFullReindexAsync(AppDb db, JobQueue jobs, CancellationToken ct = default)
    {
        if (await db.Jobs.AnyAsync(job => job.Type == nameof(ReindexSearchBatch)
                && (job.Status == JobStatus.Pending || job.Status == JobStatus.Running), ct)) return false;
        await jobs.EnqueueAsync(db, new ReindexSearchBatch(), ct: ct);
        return true;
    }

    public static async Task ProcessReindexBatchAsync(AppDb db, JobQueue jobs, MeilisearchTaskClient client,
        OptionalServiceNamespace serviceNamespace, Guid afterProductId, CancellationToken ct = default)
    {
        var laneLeaseAt = DateTime.UtcNow.AddTicks(Random.Shared.Next(1, 10_000));
        while (true)
        {
            var now = DateTime.UtcNow;
            laneLeaseAt = now.AddTicks(Random.Shared.Next(1, 10_000));
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "CronLeases" ("Name", "LastRunAt", "LockedUntil") VALUES ({{LaneName}}, NULL, NULL)
                ON CONFLICT ("Name") DO NOTHING
                """, ct);
            if (await db.CronLeases.Where(lease => lease.Name == LaneName && (lease.LockedUntil == null || lease.LockedUntil < now))
                    .ExecuteUpdateAsync(set => set.SetProperty(lease => lease.LastRunAt, laneLeaseAt)
                        .SetProperty(lease => lease.LockedUntil, now + LaneDuration), ct) == 1) break;
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }

        try
        {
            var indexUid = serviceNamespace.MeilisearchIndex("products");
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "SearchIndexStates" ("ProductId", "RequestedVersion", "AppliedVersion", "TaskId", "TaskVersion", "LeaseToken", "LockedUntil")
                VALUES ({{Guid.Empty}}, 0, 0, NULL, NULL, NULL, NULL) ON CONFLICT ("ProductId") DO NOTHING
                """, ct);
            await client.EnsureIndexAsync(indexUid, "id", ct);
            var ids = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(product => product.Id > afterProductId)
                .OrderBy(product => product.Id).Select(product => product.Id).Take(ReconciliationBatchSize).ToListAsync(ct);
            if (ids.Count == 0) return;

            var products = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(product => ids.Contains(product.Id))
                .Select(product => new
                {
                    product.Id,
                    product.Name,
                    product.Summary,
                    product.Brand,
                    product.Keywords,
                    product.SpecsJson,
                    CategoryName = product.Category.Name,
                    PriceMin = Pricing.Display(product.Price, product.DiscountPercent),
                    product.RatingAvg,
                    product.Status,
                    product.IndexVersion,
                })
                .ToListAsync(ct);
            var states = await db.SearchIndexStates.Where(state => ids.Contains(state.ProductId)).ToListAsync(ct);
            foreach (var taskId in states.Where(state => state.TaskId.HasValue).Select(state => state.TaskId!.Value).Distinct())
            {
                try { await client.WaitForSuccessAsync(new MeilisearchTaskId(taskId), cancellationToken: ct); }
                catch (MeilisearchTaskFailedException)
                {
                    foreach (var state in states.Where(row => row.TaskId == taskId)) { state.TaskId = null; state.TaskVersion = null; }
                    await db.SaveChangesAsync(ct);
                    throw;
                }
                foreach (var state in states.Where(row => row.TaskId == taskId))
                {
                    if (state.TaskVersion is { } completedVersion) state.AppliedVersion = completedVersion;
                    state.TaskId = null;
                    state.TaskVersion = null;
                }
            }

            var byId = states.ToDictionary(state => state.ProductId);
            var productById = products.ToDictionary(product => product.Id);
            foreach (var id in ids)
            {
                if (!byId.TryGetValue(id, out var state))
                {
                    state = new SearchIndexState { ProductId = id, AppliedVersion = 0, RequestedVersion = Math.Max(productById[id].IndexVersion, 1) };
                    db.SearchIndexStates.Add(state);
                    byId.Add(id, state);
                }
            }
            await db.SaveChangesAsync(ct);

            var upsertProducts = products.Where(product => product.Status == ProductStatuses.Active).ToArray();
            var upsertIds = upsertProducts.Select(product => product.Id).ToArray();
            if (upsertProducts.Length > 0)
            {
                var documents = upsertProducts.Select(product => new SearchProductDocument(
                    product.Id.ToString(),
                    $"{product.Name} {product.Summary}",
                    product.Brand,
                    product.CategoryName,
                    SearchProductTags(product.Keywords),
                    SearchProductSpecs(product.SpecsJson),
                    product.PriceMin,
                    product.RatingAvg)).ToArray();
                await SubmitBatchAsync(upsertIds, documents);
            }
            var deleteIds = products.Where(product => product.Status != ProductStatuses.Active).Select(product => product.Id).ToArray();
            if (deleteIds.Length > 0) await SubmitBatchAsync(deleteIds, null);

            if (ids.Count == ReconciliationBatchSize)
                await jobs.EnqueueAsync(db, new ReindexSearchBatch(ids[^1]), ct: ct);

            async Task SubmitBatchAsync(Guid[] batchIds, SearchProductDocument[]? documents)
            {
                var task = documents is { Length: > 0 }
                    ? await client.EnqueueUpsertAsync(indexUid, documents, cancellationToken: ct)
                    : await client.EnqueueDeleteBatchAsync(indexUid, batchIds.Select(id => id.ToString()).ToArray(), ct);
                var batchStates = batchIds.Select(id => byId[id]).ToArray();
                foreach (var state in batchStates) { state.TaskId = task.Value; state.TaskVersion = productById[state.ProductId].IndexVersion; }
                await db.SaveChangesAsync(ct);
                try { await client.WaitForSuccessAsync(task, cancellationToken: ct); }
                catch (MeilisearchTaskFailedException)
                {
                    foreach (var state in batchStates) { state.TaskId = null; state.TaskVersion = null; }
                    await db.SaveChangesAsync(ct);
                    throw;
                }
                foreach (var state in batchStates)
                {
                    if (state.TaskVersion is { } completedVersion) state.AppliedVersion = completedVersion;
                    state.TaskId = null;
                    state.TaskVersion = null;
                }
                await db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            await db.CronLeases.Where(lease => lease.Name == LaneName && lease.LastRunAt == laneLeaseAt)
                .ExecuteUpdateAsync(set => set.SetProperty(lease => lease.LockedUntil, (DateTime?)null), CancellationToken.None);
        }
    }

    /// <summary>Storefront keywords ("sennheiser bluetooth anc") become Meili tags.</summary>
    public static string[] SearchProductTags(string? keywords) => (keywords ?? "")
        .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Take(20).ToArray();

    /// <summary>[{"label","value"}] spec rows become flat "Label: Value" strings for full-text search.</summary>
    public static string[] SearchProductSpecs(string specsJson)
    {
        var rows = SearchSpecsJson(specsJson);
        return rows.Take(30).Select(row => $"{row.Label}: {row.Value}").ToArray();
    }

    public static List<SpecRow> SearchSpecsJson(string specsJson)
    {
        try
        {
            return string.IsNullOrWhiteSpace(specsJson)
                ? []
                : System.Text.Json.JsonSerializer.Deserialize<List<SpecRow>>(specsJson) ?? [];
        }
        catch (System.Text.Json.JsonException) { return []; }
    }

    public sealed record SpecRow(string Label, string Value);
}
