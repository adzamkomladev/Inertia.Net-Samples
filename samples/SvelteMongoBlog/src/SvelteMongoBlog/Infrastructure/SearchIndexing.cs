using System.Globalization;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Features.Lots;

namespace SvelteMongoBlog.Infrastructure;

public sealed record SyncLot(Guid LotId) : IJob;
public sealed record SyncLotsBatch : IJob;
public sealed record ReindexSearchBatch(Guid AfterLotId = default) : IJob;

public sealed record SearchLotDocument(
    string Id,
    string Name,
    string Address,
    string Area,
    string[] Tags,
    string[] SpotTypes,
    long HourlyRate,
    long DailyMax);

/// <summary>Durable, version-fenced Meilisearch work serialized through a Mongo lease (ported from the posts machinery to lots).</summary>
public static class SearchIndexing
{
    public const int ReconciliationBatchSize = 500;
    static readonly Guid SetupLotId = Guid.Empty;
    const string LaneName = "search-index-lane";
    static readonly TimeSpan LaneLease = TimeSpan.FromMinutes(10);
    static readonly TimeSpan LotLease = TimeSpan.FromMinutes(10);
    static readonly JsonSerializerOptions JobJson = new(JsonSerializerDefaults.Web);

    public static string Index(OptionalServiceNamespace names) => names.MeilisearchIndex("lots");

    public static bool IsMeilisearchEnabled(IConfiguration configuration) =>
        string.Equals(configuration["SEARCH_DRIVER"], "meilisearch", StringComparison.OrdinalIgnoreCase);

    /// <summary>Persists intent and optionally queues immediate work. Never calls Meilisearch.</summary>
    public static Task RequestAsync(AppDb db, JobQueue jobs, Guid lotId, long version, CancellationToken ct = default) =>
        RequestAsync(db, jobs, lotId, version, enqueue: true, ct);

    public static async Task RequestAsync(AppDb db, JobQueue jobs, Guid lotId, long version, bool enqueue, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(jobs);
        ValidateRequest(lotId, version);
        await UpsertRequestedVersionAsync(db, lotId, version, ct);
        if (enqueue) await EnqueueBatchAsync(db, jobs, null, ct);
    }

    /// <summary>Session overload keeps the lot mutation, intent, and job in one Mongo transaction.</summary>
    public static Task RequestAsync(AppDb db, JobQueue jobs, Guid lotId, long version, IClientSessionHandle session, CancellationToken ct = default) =>
        RequestAsync(db, jobs, lotId, version, session, enqueue: true, ct);

    public static async Task RequestAsync(AppDb db, JobQueue jobs, Guid lotId, long version, IClientSessionHandle session, bool enqueue, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(session);
        ValidateRequest(lotId, version);
        await db.SearchIndexStates.UpdateOneAsync(session, x => x.LotId == lotId,
            Builders<SearchIndexState>.Update.SetOnInsert(x => x.LotId, lotId)
                .SetOnInsert(x => x.AppliedVersion, 0).Max(x => x.RequestedVersion, version),
            new UpdateOptions { IsUpsert = true }, ct);
        if (enqueue) await EnqueueBatchAsync(db, jobs, session, ct);
    }

    /// <summary>Backfills missing intent and requeues a bounded set of unfinished versions.</summary>
    public static async Task<int> ReconcileAsync(AppDb db, JobQueue jobs, CancellationToken ct = default, int batchSize = ReconciliationBatchSize)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(jobs);
        if (batchSize is < 1 or > ReconciliationBatchSize) throw new ArgumentOutOfRangeException(nameof(batchSize));

        var rawLots = db.Database.GetCollection<BsonDocument>("lots");
        var missing = await rawLots.Aggregate()
            .Lookup("search_index_states", "_id", "_id", "existingState")
            .Match(new BsonDocument("existingState.0", new BsonDocument("$exists", false)))
            .Sort(new BsonDocument("_id", 1))
            .Limit(batchSize)
            .Project(new BsonDocument("_id", 1))
            .ToListAsync(ct);
        var missingIds = missing.Where(x => x.TryGetValue("_id", out var id) && id.IsGuid)
            .Select(x => x["_id"].AsGuid).ToArray();
        if (missingIds.Length > 0)
        {
            var lots = await db.Lots.Find(x => missingIds.Contains(x.Id))
                .Project(x => new { x.Id, x.IndexVersion }).ToListAsync(ct);
            foreach (var lot in lots)
                await UpsertRequestedVersionAsync(db, lot.Id, Math.Max(1, lot.IndexVersion), ct);
        }

        var now = DateTime.UtcNow;
        var due = await db.SearchIndexStates.Find(x => x.LotId != Guid.Empty && x.RequestedVersion > x.AppliedVersion
            && (x.LockedUntil == null || x.LockedUntil < now)).AnyAsync(ct);
        return missingIds.Length + (due && await EnqueueBatchAsync(db, jobs, null, ct) ? 1 : 0);
    }

    public static async Task<bool> EnqueueBatchAsync(AppDb db, JobQueue jobs, IClientSessionHandle? session = null, CancellationToken ct = default)
    {
        var filter = Builders<Job>.Filter.Where(x => x.Type == nameof(SyncLotsBatch)
            && (x.Status == JobStatus.Pending || x.Status == JobStatus.Running));
        var active = session is null ? await db.Jobs.Find(filter).AnyAsync(ct) : await db.Jobs.Find(session, filter).AnyAsync(ct);
        if (active) return false;
        await jobs.EnqueueAsync(db, new SyncLotsBatch(), session: session, ct: ct);
        return true;
    }

    public static async Task ProcessBatchAsync(AppDb db, JobQueue jobs, MeilisearchTaskClient client, OptionalServiceNamespace names, CancellationToken ct = default)
    {
        var token = await AcquireLaneAsync(db, ct);
        try
        {
            await EnsureConfiguredIndexAsync(db, client, names, token, ct);
            var now = DateTime.UtcNow;
            var ids = await db.SearchIndexStates.Find(x => x.LotId != Guid.Empty && x.RequestedVersion > x.AppliedVersion
                && (x.LockedUntil == null || x.LockedUntil < now)).SortBy(x => x.LotId)
                .Limit(ReconciliationBatchSize).Project(x => x.LotId).ToListAsync(ct);
            if (ids.Count == 0) return;
            await db.SearchIndexStates.UpdateManyAsync(x => ids.Contains(x.LotId) && (x.LockedUntil == null || x.LockedUntil < now),
                Builders<SearchIndexState>.Update.Set(x => x.LeaseToken, token).Set(x => x.LockedUntil, now + LotLease), cancellationToken: ct);
            var claimed = await db.SearchIndexStates.Find(x => ids.Contains(x.LotId) && x.LeaseToken == token).ToListAsync(ct);
            var claimedIds = claimed.Select(x => x.LotId).ToArray();
            await CompleteBatchPendingTasksAsync(db, client, claimedIds, token, ct);
            var states = await db.SearchIndexStates.Find(x => claimedIds.Contains(x.LotId) && x.RequestedVersion > x.AppliedVersion).ToListAsync(ct);
            var versions = states.ToDictionary(x => x.LotId, x => x.RequestedVersion);
            var dirtyIds = states.Select(x => x.LotId).ToArray();
            var lots = await db.Lots.Find(x => dirtyIds.Contains(x.Id)).ToListAsync(ct);
            var active = lots.Where(x => x.IsActive).ToArray();
            var activeIds = active.Select(x => x.Id).ToArray();
            var typeRows = activeIds.Length == 0 ? [] : await db.Spots.Find(x => activeIds.Contains(x.LotId) && x.Active)
                .Project(x => new { x.LotId, x.Type }).ToListAsync(ct);
            var typesByLot = typeRows.GroupBy(x => x.LotId)
                .ToDictionary(g => g.Key, g => g.Select(x => TypeTag(x.Type)).Distinct().OrderBy(x => x).ToArray());
            var documents = active.Select(lot => ToDocument(lot, typesByLot.GetValueOrDefault(lot.Id, []))).ToArray();
            if (documents.Length > 0) await Submit(activeIds, documents);
            var deleted = dirtyIds.Except(activeIds).ToArray();
            if (deleted.Length > 0) await Submit(deleted, null);

            async Task Submit(Guid[] lotIds, SearchLotDocument[]? docs)
            {
                await AssertLaneAsync(db, token, ct);
                var task = docs is not null ? await client.EnqueueUpsertAsync(Index(names), docs, cancellationToken: ct)
                    : await client.EnqueueDeleteBatchAsync(Index(names), lotIds.Select(id => id.ToString()).ToArray(), ct);
                await PersistBatchTaskAsync(db, lotIds, token, task.Value, versions, ct);
                var rows = await db.SearchIndexStates.Find(x => lotIds.Contains(x.LotId) && x.LeaseToken == token && x.TaskId == task.Value).ToListAsync(ct);
                await CompleteBatchTaskAsync(db, client, rows, token, ct);
            }
        }
        finally
        {
            await db.SearchIndexStates.UpdateManyAsync(x => x.LotId != Guid.Empty && x.LeaseToken == token,
                Builders<SearchIndexState>.Update.Set(x => x.LeaseToken, null).Set(x => x.LockedUntil, null), cancellationToken: CancellationToken.None);
            await ReleaseLaneAsync(db, token);
        }
        if (await db.SearchIndexStates.Find(x => x.LotId != Guid.Empty && x.RequestedVersion > x.AppliedVersion
                && (x.LockedUntil == null || x.LockedUntil < DateTime.UtcNow)).AnyAsync(ct))
            await jobs.EnqueueAsync(db, new SyncLotsBatch(), ct: ct);
    }

    /// <summary>Processes the latest requested version while holding the shared search lane.</summary>
    public static async Task SyncAsync(AppDb db, MeilisearchTaskClient client, OptionalServiceNamespace names, Guid lotId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(names);
        if (lotId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(lotId));

        var token = await AcquireLaneAsync(db, ct);
        var lockedLot = false;
        try
        {
            await EnsureConfiguredIndexAsync(db, client, names, token, ct);
            var state = await db.SearchIndexStates.Find(x => x.LotId == lotId).FirstOrDefaultAsync(ct);
            if (state is null) return;
            var now = DateTime.UtcNow;
            var claim = await db.SearchIndexStates.UpdateOneAsync(x => x.LotId == lotId
                    && (x.LockedUntil == null || x.LockedUntil < now || x.LeaseToken == token),
                Builders<SearchIndexState>.Update.Set(x => x.LeaseToken, token).Set(x => x.LockedUntil, now + LotLease), cancellationToken: ct);
            if (claim.ModifiedCount == 0) throw new InvalidOperationException("Search lot lease is held; retry later.");
            lockedLot = true;

            while (true)
            {
                await AssertLaneAsync(db, token, ct);
                state = await db.SearchIndexStates.Find(x => x.LotId == lotId && x.LeaseToken == token).FirstOrDefaultAsync(ct);
                if (state is null) return;
                if (state.TaskId is { } pending)
                {
                    await CompleteTaskAsync(db, client, state, token, ct);
                    continue;
                }
                if (state.RequestedVersion <= state.AppliedVersion) return;

                var version = state.RequestedVersion;
                var lot = await db.Lots.Find(x => x.Id == lotId && x.IsActive).FirstOrDefaultAsync(ct);
                MeilisearchTaskId task;
                if (lot is null)
                {
                    task = await client.EnqueueDeleteAsync(Index(names), lotId.ToString(), ct);
                }
                else
                {
                    var types = await db.Spots.Find(x => x.LotId == lotId && x.Active)
                        .Project(x => x.Type).ToListAsync(ct);
                    var document = ToDocument(lot, types.Select(TypeTag).Distinct().OrderBy(x => x).ToArray());
                    task = await client.EnqueueUpsertAsync(Index(names), [document], cancellationToken: ct);
                }
                await SaveTaskAsync(db, lotId, token, task.Value, version, ct);
                var refreshed = await db.SearchIndexStates.Find(x => x.LotId == lotId && x.LeaseToken == token).FirstOrDefaultAsync(ct);
                if (refreshed is null) return;
                await CompleteTaskAsync(db, client, refreshed, token, ct);
                var latest = await db.SearchIndexStates.Find(x => x.LotId == lotId).FirstAsync(ct);
                if (latest.RequestedVersion <= latest.AppliedVersion) return;
            }
        }
        finally
        {
            if (lockedLot)
                await db.SearchIndexStates.UpdateOneAsync(x => x.LotId == lotId && x.LeaseToken == token,
                    Builders<SearchIndexState>.Update.Set(x => x.LeaseToken, null).Set(x => x.LockedUntil, null), cancellationToken: CancellationToken.None);
            await ReleaseLaneAsync(db, token);
        }
    }

    /// <summary>Request one resumable full reindex. The outbox and worker stay active across restarts.</summary>
    public static async Task<bool> RequestFullReindexAsync(AppDb db, JobQueue jobs, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(jobs);
        if (await IsReindexInProgressAsync(db, ct)) return false;
        await jobs.EnqueueAsync(db, new ReindexSearchBatch(), ct: ct);
        jobs.Wake();
        return true;
    }

    public static Task<bool> IsReindexInProgressAsync(AppDb db, CancellationToken ct = default) =>
        db.Jobs.Find(x => x.Type == nameof(ReindexSearchBatch)
            && (x.Status == JobStatus.Pending || x.Status == JobStatus.Running)).AnyAsync(ct);

    /// <summary>Reindexes at most 500 Mongo lots in bulk and persists each Meilisearch task before polling it.</summary>
    public static async Task ProcessReindexBatchAsync(
        AppDb db,
        JobQueue jobs,
        MeilisearchTaskClient client,
        OptionalServiceNamespace names,
        Guid afterLotId,
        CancellationToken ct = default)
    {
        var lots = await db.Lots.Find(Builders<Lot>.Filter.Gt(x => x.Id, afterLotId)).SortBy(x => x.Id)
            .Limit(ReconciliationBatchSize).ToListAsync(ct);
        if (lots.Count == 0) return;

        var token = await AcquireLaneAsync(db, ct);
        var claimedIds = lots.Select(lot => lot.Id).ToArray();
        var versions = lots.ToDictionary(lot => lot.Id, lot => Math.Max(1, lot.IndexVersion));
        try
        {
            await EnsureConfiguredIndexAsync(db, client, names, token, ct);
            var upserts = lots.Select(lot => new UpdateOneModel<SearchIndexState>(
                Builders<SearchIndexState>.Filter.Eq(row => row.LotId, lot.Id),
                Builders<SearchIndexState>.Update.SetOnInsert(row => row.LotId, lot.Id)
                    .SetOnInsert(row => row.AppliedVersion, 0).Max(row => row.RequestedVersion, versions[lot.Id])) { IsUpsert = true }).ToArray();
            if (upserts.Length > 0) await db.SearchIndexStates.BulkWriteAsync(upserts, new BulkWriteOptions { IsOrdered = false }, ct);
            var leaseNow = DateTime.UtcNow;
            var leaseWrites = lots.Select(lot => new UpdateOneModel<SearchIndexState>(
                Builders<SearchIndexState>.Filter.Eq(row => row.LotId, lot.Id),
                Builders<SearchIndexState>.Update.Set(row => row.LeaseToken, token).Set(row => row.LockedUntil, leaseNow + LotLease))).ToArray();
            if (leaseWrites.Length > 0) await db.SearchIndexStates.BulkWriteAsync(leaseWrites, new BulkWriteOptions { IsOrdered = false }, ct);

            await CompleteBatchPendingTasksAsync(db, client, claimedIds, token, ct);
            await AssertLaneAsync(db, token, ct);

            var typeRows = await db.Spots.Find(x => claimedIds.Contains(x.LotId) && x.Active)
                .Project(x => new { x.LotId, x.Type }).ToListAsync(ct);
            var typesByLot = typeRows.GroupBy(x => x.LotId)
                .ToDictionary(g => g.Key, g => g.Select(x => TypeTag(x.Type)).Distinct().OrderBy(x => x).ToArray());
            var activeLots = lots.Where(lot => lot.IsActive).ToArray();
            var activeIds = activeLots.Select(lot => lot.Id).ToArray();
            var docs = activeLots.Select(lot => ToDocument(lot, typesByLot.GetValueOrDefault(lot.Id, []))).ToArray();
            var inactiveIds = lots.Where(lot => !lot.IsActive).Select(lot => lot.Id).ToArray();
            if (docs.Length > 0)
                await SubmitBatchAsync(docs.Select(document => Guid.Parse(document.Id)).ToArray(), docs);
            if (inactiveIds.Length > 0)
                await SubmitDeleteBatchAsync(inactiveIds);

            await QueueNewerVersionsAsync(db, jobs, claimedIds, ct);
            if (lots.Count == ReconciliationBatchSize)
                await EnqueueContinuationAsync(db, jobs, lots[^1].Id, ct);
        }
        finally
        {
            if (claimedIds.Length > 0)
            {
                var releaseRows = claimedIds.Select(id => new UpdateOneModel<SearchIndexState>(
                    Builders<SearchIndexState>.Filter.Eq(row => row.LotId, id) & Builders<SearchIndexState>.Filter.Eq(row => row.LeaseToken, token),
                    Builders<SearchIndexState>.Update.Set(row => row.LeaseToken, null).Set(row => row.LockedUntil, null))).ToArray();
                await db.SearchIndexStates.BulkWriteAsync(releaseRows, new BulkWriteOptions { IsOrdered = false }, CancellationToken.None);
            }
            await ReleaseLaneAsync(db, token);
        }

        async Task SubmitBatchAsync(Guid[] lotIds, SearchLotDocument[] documents)
        {
            await AssertLaneAsync(db, token, ct);
            var task = await client.EnqueueUpsertAsync(Index(names), documents, cancellationToken: ct);
            await PersistBatchTaskAsync(db, lotIds, token, task.Value, versions, ct);
            var rows = await db.SearchIndexStates.Find(x => lotIds.Contains(x.LotId) && x.TaskId == task.Value && x.LeaseToken == token).ToListAsync(ct);
            await CompleteBatchTaskAsync(db, client, rows, token, ct);
        }

        async Task SubmitDeleteBatchAsync(Guid[] lotIds)
        {
            await AssertLaneAsync(db, token, ct);
            var documentIds = lotIds.Select(id => id.ToString()).ToArray();
            var task = await client.EnqueueDeleteBatchAsync(Index(names), documentIds, ct);
            await PersistBatchTaskAsync(db, lotIds, token, task.Value, versions, ct);
            var rows = await db.SearchIndexStates.Find(x => lotIds.Contains(x.LotId) && x.TaskId == task.Value && x.LeaseToken == token).ToListAsync(ct);
            await CompleteBatchTaskAsync(db, client, rows, token, ct);
        }
    }

    static async Task EnsureConfiguredIndexAsync(AppDb db, MeilisearchTaskClient client, OptionalServiceNamespace names, string token, CancellationToken ct)
    {
        await EnsureSearchStateAsync(db, SetupLotId, ct);
        while (true)
        {
            await AssertLaneAsync(db, token, ct);
            var state = await db.SearchIndexStates.Find(x => x.LotId == SetupLotId).FirstAsync(ct);
            if (state.TaskId is { } pending)
            {
                var stage = state.TaskVersion ?? 1;
                try
                {
                    if (stage == 1)
                    {
                        try { await client.WaitForSuccessAsync(new MeilisearchTaskId(pending), cancellationToken: ct); }
                        catch (MeilisearchTaskFailedException error) when (error.Type == "indexCreation" && error.ErrorCode == "index_already_exists")
                        {
                            if (!string.Equals(await client.GetIndexPrimaryKeyAsync(Index(names), ct), "id", StringComparison.Ordinal)) throw;
                        }
                    }
                    else await client.WaitForSuccessAsync(new MeilisearchTaskId(pending), cancellationToken: ct);
                }
                catch (MeilisearchTaskFailedException)
                {
                    await db.SearchIndexStates.UpdateOneAsync(x => x.LotId == SetupLotId && x.LeaseToken == token && x.TaskId == pending,
                        Builders<SearchIndexState>.Update.Set(x => x.TaskId, null).Set(x => x.TaskVersion, null), cancellationToken: ct);
                    throw;
                }
                await db.SearchIndexStates.UpdateOneAsync(x => x.LotId == SetupLotId && x.LeaseToken == token && x.TaskId == pending,
                    Builders<SearchIndexState>.Update.Max(x => x.AppliedVersion, stage).Set(x => x.TaskId, null).Set(x => x.TaskVersion, null), cancellationToken: ct);
                continue;
            }

            if (state.AppliedVersion < 1)
            {
                var task = await client.EnsureIndexAsync(Index(names), "id", ct);
                if (task is { } create)
                {
                    await db.SearchIndexStates.UpdateOneAsync(x => x.LotId == SetupLotId && x.LeaseToken == token,
                        Builders<SearchIndexState>.Update.Set(x => x.TaskId, create.Value).Set(x => x.TaskVersion, 1), cancellationToken: ct);
                    continue;
                }
                await db.SearchIndexStates.UpdateOneAsync(x => x.LotId == SetupLotId && x.LeaseToken == token,
                    Builders<SearchIndexState>.Update.Max(x => x.AppliedVersion, 1), cancellationToken: ct);
                continue;
            }
            if (state.AppliedVersion < 2)
            {
                var task = await client.EnqueueSearchSettingsAsync(Index(names), ct);
                await db.SearchIndexStates.UpdateOneAsync(x => x.LotId == SetupLotId && x.LeaseToken == token,
                    Builders<SearchIndexState>.Update.Set(x => x.TaskId, task.Value).Set(x => x.TaskVersion, 2), cancellationToken: ct);
                continue;
            }
            return;
        }
    }

    static async Task CompleteTaskAsync(AppDb db, MeilisearchTaskClient client, SearchIndexState state, string token, CancellationToken ct)
    {
        if (state.TaskId is not { } taskId) return;
        try { await client.WaitForSuccessAsync(new MeilisearchTaskId(taskId), cancellationToken: ct); }
        catch (MeilisearchTaskFailedException)
        {
            await db.SearchIndexStates.UpdateOneAsync(x => x.LotId == state.LotId && x.TaskId == taskId && x.LeaseToken == token,
                Builders<SearchIndexState>.Update.Set(x => x.TaskId, null).Set(x => x.TaskVersion, null), cancellationToken: ct);
            throw;
        }
        var update = Builders<SearchIndexState>.Update.Set(x => x.TaskId, null).Set(x => x.TaskVersion, null);
        if (state.TaskVersion is { } version) update = Builders<SearchIndexState>.Update.Max(x => x.AppliedVersion, version).Set(x => x.TaskId, null).Set(x => x.TaskVersion, null);
        await db.SearchIndexStates.UpdateOneAsync(x => x.LotId == state.LotId && x.TaskId == taskId && x.LeaseToken == token,
            update, cancellationToken: ct);
    }

    static async Task CompleteBatchPendingTasksAsync(AppDb db, MeilisearchTaskClient client, Guid[] ids, string token, CancellationToken ct)
    {
        var states = await db.SearchIndexStates.Find(x => ids.Contains(x.LotId) && x.LeaseToken == token && x.TaskId != null).ToListAsync(ct);
        foreach (var group in states.GroupBy(state => state.TaskId!.Value))
            await CompleteBatchTaskAsync(db, client, group.ToList(), token, ct);
    }

    static async Task CompleteBatchTaskAsync(AppDb db, MeilisearchTaskClient client, IReadOnlyCollection<SearchIndexState> states, string token, CancellationToken ct)
    {
        if (states.Count == 0) return;
        var taskId = states.First().TaskId;
        if (taskId is null || states.Any(row => row.TaskId != taskId)) throw new InvalidOperationException("Search task group is inconsistent.");
        try { await client.WaitForSuccessAsync(new MeilisearchTaskId(taskId.Value), cancellationToken: ct); }
        catch (MeilisearchTaskFailedException)
        {
            var clear = states.Select(row => new UpdateOneModel<SearchIndexState>(
                Builders<SearchIndexState>.Filter.Eq(x => x.LotId, row.LotId) & Builders<SearchIndexState>.Filter.Eq(x => x.TaskId, taskId) & Builders<SearchIndexState>.Filter.Eq(x => x.LeaseToken, token),
                Builders<SearchIndexState>.Update.Set(x => x.TaskId, null).Set(x => x.TaskVersion, null))).ToArray();
            await db.SearchIndexStates.BulkWriteAsync(clear, new BulkWriteOptions { IsOrdered = false }, ct);
            throw;
        }
        var updates = states.Select(row =>
        {
            var update = Builders<SearchIndexState>.Update.Set(x => x.TaskId, null).Set(x => x.TaskVersion, null);
            if (row.TaskVersion is { } version) update = Builders<SearchIndexState>.Update.Max(x => x.AppliedVersion, version).Set(x => x.TaskId, null).Set(x => x.TaskVersion, null);
            return new UpdateOneModel<SearchIndexState>(
                Builders<SearchIndexState>.Filter.Eq(x => x.LotId, row.LotId) & Builders<SearchIndexState>.Filter.Eq(x => x.TaskId, taskId) & Builders<SearchIndexState>.Filter.Eq(x => x.LeaseToken, token), update);
        }).ToArray();
        await db.SearchIndexStates.BulkWriteAsync(updates, new BulkWriteOptions { IsOrdered = false }, ct);
    }

    static async Task SaveTaskAsync(AppDb db, Guid lotId, string token, long taskId, long version, CancellationToken ct)
    {
        var result = await db.SearchIndexStates.UpdateOneAsync(x => x.LotId == lotId && x.LeaseToken == token,
            Builders<SearchIndexState>.Update.Set(x => x.TaskId, taskId).Set(x => x.TaskVersion, version), cancellationToken: ct);
        if (result.ModifiedCount == 0) throw new InvalidOperationException("Search lot lease was lost before task persistence.");
    }

    static async Task PersistBatchTaskAsync(AppDb db, Guid[] lotIds, string token, long taskId, IReadOnlyDictionary<Guid, long> versions, CancellationToken ct)
    {
        var updates = lotIds.Select(id => new UpdateOneModel<SearchIndexState>(
            Builders<SearchIndexState>.Filter.Eq(x => x.LotId, id) & Builders<SearchIndexState>.Filter.Eq(x => x.LeaseToken, token),
            Builders<SearchIndexState>.Update.Set(x => x.TaskId, taskId).Set(x => x.TaskVersion, versions[id]))).ToArray();
        if (updates.Length > 0) await db.SearchIndexStates.BulkWriteAsync(updates, new BulkWriteOptions { IsOrdered = false }, ct);
    }

    static async Task QueueNewerVersionsAsync(AppDb db, JobQueue jobs, Guid[] ids, CancellationToken ct)
    {
        var states = await db.SearchIndexStates.Find(x => ids.Contains(x.LotId) && x.RequestedVersion > x.AppliedVersion).Project(x => x.LotId).ToListAsync(ct);
        if (states.Count > 0) await EnqueueBatchAsync(db, jobs, null, ct);
    }

    static async Task EnqueueContinuationAsync(AppDb db, JobQueue jobs, Guid afterLotId, CancellationToken ct)
    {
        var payloads = await db.Jobs.Find(x => x.Type == nameof(ReindexSearchBatch)
                && (x.Status == JobStatus.Pending || x.Status == JobStatus.Running))
            .Project(x => x.Payload).ToListAsync(ct);
        foreach (var payload in payloads)
        {
            try
            {
                if (JsonSerializer.Deserialize<ReindexSearchBatch>(payload, JobJson) is { } job && job.AfterLotId == afterLotId) return;
            }
            catch (JsonException) { }
        }
        await jobs.EnqueueAsync(db, new ReindexSearchBatch(afterLotId), ct: ct);
        jobs.Wake();
    }

    internal static SearchLotDocument ToDocument(Lot lot, IReadOnlyCollection<string> spotTypes) => new(
        lot.Id.ToString(), lot.Name, lot.Address, lot.Area,
        Tags(lot, spotTypes), spotTypes.ToArray(), lot.RatePerHourCents, lot.DailyMaxCents);

    static string[] Tags(Lot lot, IReadOnlyCollection<string> spotTypes) =>
        [
            .. spotTypes.Select(t => t.ToLowerInvariant()),
            "spot-cover",
            lot.Area.ToLowerInvariant().Replace(' ', '-')
        ];

    internal static string TypeTag(SpotType type) => type switch
    {
        SpotType.Standard => "standard",
        SpotType.Compact => "compact",
        SpotType.Handicap => "handicap",
        SpotType.Covered => "covered",
        SpotType.Ev => "ev",
        _ => "standard"
    };

    static async Task UpsertRequestedVersionAsync(AppDb db, Guid lotId, long version, CancellationToken ct)
    {
        var update = Builders<SearchIndexState>.Update.SetOnInsert(x => x.LotId, lotId)
            .SetOnInsert(x => x.AppliedVersion, 0).Max(x => x.RequestedVersion, version);
        try { await db.SearchIndexStates.UpdateOneAsync(x => x.LotId == lotId, update, new UpdateOptions { IsUpsert = true }, ct); }
        catch (MongoWriteException error) when (error.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        { await db.SearchIndexStates.UpdateOneAsync(x => x.LotId == lotId, Builders<SearchIndexState>.Update.Max(x => x.RequestedVersion, version), cancellationToken: ct); }
    }

    static async Task EnsureSearchStateAsync(AppDb db, Guid lotId, CancellationToken ct)
    {
        try
        {
            await db.SearchIndexStates.UpdateOneAsync(x => x.LotId == lotId,
                Builders<SearchIndexState>.Update.SetOnInsert(x => x.LotId, lotId).SetOnInsert(x => x.AppliedVersion, 0).SetOnInsert(x => x.RequestedVersion, 0),
                new UpdateOptions { IsUpsert = true }, ct);
        }
        catch (MongoWriteException error) when (error.WriteError?.Category == ServerErrorCategory.DuplicateKey) { }
    }

    static async Task<string> AcquireLaneAsync(AppDb db, CancellationToken ct)
    {
        await EnsureSearchStateAsync(db, SetupLotId, ct);
        var token = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var lane = await db.SearchIndexStates.FindOneAndUpdateAsync(
            x => x.LotId == SetupLotId && (x.LockedUntil == null || x.LockedUntil < now),
            Builders<SearchIndexState>.Update.Set(x => x.LeaseToken, token).Set(x => x.LockedUntil, now + LaneLease),
            new FindOneAndUpdateOptions<SearchIndexState> { ReturnDocument = ReturnDocument.After }, ct);
        if (lane is null) throw new InvalidOperationException($"The {LaneName} lease is held; retry later.");
        return token;
    }

    static async Task AssertLaneAsync(AppDb db, string token, CancellationToken ct)
    {
        if (!await db.SearchIndexStates.Find(x => x.LotId == SetupLotId && x.LeaseToken == token && x.LockedUntil > DateTime.UtcNow).AnyAsync(ct))
            throw new InvalidOperationException("The global search indexing lease was lost.");
    }

    static Task ReleaseLaneAsync(AppDb db, string token) => db.SearchIndexStates.UpdateOneAsync(
        x => x.LotId == SetupLotId && x.LeaseToken == token,
        Builders<SearchIndexState>.Update.Set(x => x.LeaseToken, null).Set(x => x.LockedUntil, null),
        cancellationToken: CancellationToken.None);

    static void ValidateRequest(Guid lotId, long version)
    {
        if (lotId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(lotId));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
    }
}
