using System.Globalization;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using SvelteMongoBlog.Data;

namespace SvelteMongoBlog.Infrastructure;

public sealed record SyncSearchPost(Guid PostId) : IJob;
public sealed record SyncSearchPostsBatch : IJob;
public sealed record ReindexSearchBatch(Guid AfterPostId = default) : IJob;

public sealed record SearchPostDocument(
    string Id,
    string Title,
    string Body,
    string Excerpt,
    string Slug,
    Guid AuthorId,
    string AuthorName,
    string[] Topics,
    DateTime PublishedAt);

/// <summary>Durable, version-fenced Meilisearch work serialized through a Mongo lease.</summary>
public static class SearchIndexing
{
    public const int ReconciliationBatchSize = 500;
    static readonly Guid SetupPostId = Guid.Empty;
    const string LaneName = "search-index-lane";
    static readonly TimeSpan LaneLease = TimeSpan.FromMinutes(10);
    static readonly TimeSpan PostLease = TimeSpan.FromMinutes(10);
    static readonly JsonSerializerOptions JobJson = new(JsonSerializerDefaults.Web);

    public static string Index(OptionalServiceNamespace names) => names.MeilisearchIndex("posts");

    public static bool IsMeilisearchEnabled(IConfiguration configuration) =>
        string.Equals(configuration["SEARCH_DRIVER"], "meilisearch", StringComparison.OrdinalIgnoreCase);

    /// <summary>Persists intent and optionally queues immediate work. Never calls Meilisearch.</summary>
    public static Task RequestAsync(AppDb db, JobQueue jobs, Guid postId, long version, CancellationToken ct = default) =>
        RequestAsync(db, jobs, postId, version, enqueue: true, ct);

    public static async Task RequestAsync(AppDb db, JobQueue jobs, Guid postId, long version, bool enqueue, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(jobs);
        ValidateRequest(postId, version);
        await UpsertRequestedVersionAsync(db, postId, version, ct);
        if (enqueue) await EnqueueBatchAsync(db, jobs, null, ct);
    }

    /// <summary>Session overload keeps the post mutation, intent, and job in one Mongo transaction.</summary>
    public static Task RequestAsync(AppDb db, JobQueue jobs, Guid postId, long version, IClientSessionHandle session, CancellationToken ct = default) =>
        RequestAsync(db, jobs, postId, version, session, enqueue: true, ct);

    public static async Task RequestAsync(AppDb db, JobQueue jobs, Guid postId, long version, IClientSessionHandle session, bool enqueue, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(session);
        ValidateRequest(postId, version);
        await db.SearchIndexStates.UpdateOneAsync(session, x => x.PostId == postId,
            Builders<SearchIndexState>.Update.SetOnInsert(x => x.PostId, postId)
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

        var rawPosts = db.Database.GetCollection<BsonDocument>("posts");
        var missing = await rawPosts.Aggregate()
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
            var posts = await db.Posts.Find(x => missingIds.Contains(x.Id))
                .Project(x => new { x.Id, x.IndexVersion }).ToListAsync(ct);
            foreach (var post in posts)
                await UpsertRequestedVersionAsync(db, post.Id, Math.Max(1, post.IndexVersion), ct);
        }

        var now = DateTime.UtcNow;
        var due = await db.SearchIndexStates.Find(x => x.PostId != Guid.Empty && x.RequestedVersion > x.AppliedVersion
            && (x.LockedUntil == null || x.LockedUntil < now)).AnyAsync(ct);
        return missingIds.Length + (due && await EnqueueBatchAsync(db, jobs, null, ct) ? 1 : 0);
    }

    public static async Task<bool> EnqueueBatchAsync(AppDb db, JobQueue jobs, IClientSessionHandle? session = null, CancellationToken ct = default)
    {
        var filter = Builders<Job>.Filter.Where(x => x.Type == nameof(SyncSearchPostsBatch)
            && (x.Status == JobStatus.Pending || x.Status == JobStatus.Running));
        var active = session is null ? await db.Jobs.Find(filter).AnyAsync(ct) : await db.Jobs.Find(session, filter).AnyAsync(ct);
        if (active) return false;
        await jobs.EnqueueAsync(db, new SyncSearchPostsBatch(), session: session, ct: ct);
        return true;
    }

    public static async Task ProcessBatchAsync(AppDb db, JobQueue jobs, MeilisearchTaskClient client, OptionalServiceNamespace names, CancellationToken ct = default)
    {
        var token = await AcquireLaneAsync(db, ct);
        try
        {
            await EnsureConfiguredIndexAsync(db, client, names, token, ct);
            var now = DateTime.UtcNow;
            var ids = await db.SearchIndexStates.Find(x => x.PostId != Guid.Empty && x.RequestedVersion > x.AppliedVersion
                && (x.LockedUntil == null || x.LockedUntil < now)).SortBy(x => x.PostId)
                .Limit(ReconciliationBatchSize).Project(x => x.PostId).ToListAsync(ct);
            if (ids.Count == 0) return;
            await db.SearchIndexStates.UpdateManyAsync(x => ids.Contains(x.PostId) && (x.LockedUntil == null || x.LockedUntil < now),
                Builders<SearchIndexState>.Update.Set(x => x.LeaseToken, token).Set(x => x.LockedUntil, now + PostLease), cancellationToken: ct);
            var claimed = await db.SearchIndexStates.Find(x => x.PostId != Guid.Empty && x.LeaseToken == token).ToListAsync(ct);
            var claimedIds = claimed.Select(x => x.PostId).ToArray();
            await CompleteBatchPendingTasksAsync(db, client, claimedIds, token, ct);
            var states = await db.SearchIndexStates.Find(x => claimedIds.Contains(x.PostId) && x.RequestedVersion > x.AppliedVersion).ToListAsync(ct);
            var versions = states.ToDictionary(x => x.PostId, x => x.RequestedVersion);
            var dirtyIds = states.Select(x => x.PostId).ToArray();
            var posts = await db.Posts.Find(x => dirtyIds.Contains(x.Id)).ToListAsync(ct);
            var authorIds = posts.Select(x => x.AuthorId).Distinct().ToArray();
            var authors = (await db.Users.Find(x => authorIds.Contains(x.Id)).Project(x => new { x.Id, x.Name }).ToListAsync(ct)).ToDictionary(x => x.Id, x => x.Name);
            var active = posts.Where(x => x.ArchivedAt == null).ToArray();
            var activeIds = active.Select(x => x.Id).ToArray();
            var topicRows = await db.PostTopics.Find(x => activeIds.Contains(x.PostId)).ToListAsync(ct);
            var topics = topicRows.GroupBy(x => x.PostId).ToDictionary(g => g.Key, g => g.Select(x => x.Topic).ToArray());
            var documents = active.Select(post => ToDocument(post, authors.GetValueOrDefault(post.AuthorId, "Deleted user"), topics.GetValueOrDefault(post.Id, []))).ToArray();
            if (documents.Length > 0) await Submit(activeIds, documents);
            var deleted = dirtyIds.Except(activeIds).ToArray();
            if (deleted.Length > 0) await Submit(deleted, null);

            async Task Submit(Guid[] postIds, SearchPostDocument[]? docs)
            {
                await AssertLaneAsync(db, token, ct);
                var task = docs is not null ? await client.EnqueueUpsertAsync(Index(names), docs, cancellationToken: ct)
                    : await client.EnqueueDeleteBatchAsync(Index(names), postIds.Select(id => id.ToString()).ToArray(), ct);
                await PersistBatchTaskAsync(db, postIds, token, task.Value, versions, ct);
                var rows = await db.SearchIndexStates.Find(x => postIds.Contains(x.PostId) && x.LeaseToken == token && x.TaskId == task.Value).ToListAsync(ct);
                await CompleteBatchTaskAsync(db, client, rows, token, ct);
            }
        }
        finally
        {
            await db.SearchIndexStates.UpdateManyAsync(x => x.PostId != Guid.Empty && x.LeaseToken == token,
                Builders<SearchIndexState>.Update.Set(x => x.LeaseToken, null).Set(x => x.LockedUntil, null), cancellationToken: CancellationToken.None);
            await ReleaseLaneAsync(db, token);
        }
        if (await db.SearchIndexStates.Find(x => x.PostId != Guid.Empty && x.RequestedVersion > x.AppliedVersion
                && (x.LockedUntil == null || x.LockedUntil < DateTime.UtcNow)).AnyAsync(ct))
            await jobs.EnqueueAsync(db, new SyncSearchPostsBatch(), ct: ct);
    }

    /// <summary>Processes the latest requested version while holding the shared search lane.</summary>
    public static async Task SyncAsync(AppDb db, MeilisearchTaskClient client, OptionalServiceNamespace names, Guid postId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(names);
        if (postId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(postId));

        var token = await AcquireLaneAsync(db, ct);
        var lockedPost = false;
        try
        {
            await EnsureConfiguredIndexAsync(db, client, names, token, ct);
            var state = await db.SearchIndexStates.Find(x => x.PostId == postId).FirstOrDefaultAsync(ct);
            if (state is null) return;
            var now = DateTime.UtcNow;
            var claim = await db.SearchIndexStates.UpdateOneAsync(x => x.PostId == postId
                    && (x.LockedUntil == null || x.LockedUntil < now || x.LeaseToken == token),
                Builders<SearchIndexState>.Update.Set(x => x.LeaseToken, token).Set(x => x.LockedUntil, now + PostLease), cancellationToken: ct);
            if (claim.ModifiedCount == 0) throw new InvalidOperationException("Search post lease is held; retry later.");
            lockedPost = true;

            while (true)
            {
                await AssertLaneAsync(db, token, ct);
                state = await db.SearchIndexStates.Find(x => x.PostId == postId && x.LeaseToken == token).FirstOrDefaultAsync(ct);
                if (state is null) return;
                if (state.TaskId is { } pending)
                {
                    await CompleteTaskAsync(db, client, state, token, ct);
                    continue;
                }
                if (state.RequestedVersion <= state.AppliedVersion) return;

                var version = state.RequestedVersion;
                var post = await db.Posts.Find(x => x.Id == postId && x.ArchivedAt == null).FirstOrDefaultAsync(ct);
                MeilisearchTaskId task;
                if (post is null)
                {
                    task = await client.EnqueueDeleteAsync(Index(names), postId.ToString(), ct);
                }
                else
                {
                    var author = await db.Users.Find(x => x.Id == post.AuthorId).FirstOrDefaultAsync(ct);
                    var topics = await db.PostTopics.Find(x => x.PostId == postId).Project(x => x.Topic).ToListAsync(ct);
                    var document = ToDocument(post, author?.Name ?? "Deleted user", topics);
                    task = await client.EnqueueUpsertAsync(Index(names), [document], cancellationToken: ct);
                }
                await SaveTaskAsync(db, postId, token, task.Value, version, ct);
                var refreshed = await db.SearchIndexStates.Find(x => x.PostId == postId && x.LeaseToken == token).FirstOrDefaultAsync(ct);
                if (refreshed is null) return;
                await CompleteTaskAsync(db, client, refreshed, token, ct);
                var latest = await db.SearchIndexStates.Find(x => x.PostId == postId).FirstAsync(ct);
                if (latest.RequestedVersion <= latest.AppliedVersion) return;
            }
        }
        finally
        {
            if (lockedPost)
                await db.SearchIndexStates.UpdateOneAsync(x => x.PostId == postId && x.LeaseToken == token,
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

    /// <summary>Reindexes at most 500 Mongo posts in bulk and persists each Meilisearch task before polling it.</summary>
    public static async Task ProcessReindexBatchAsync(
        AppDb db,
        JobQueue jobs,
        MeilisearchTaskClient client,
        OptionalServiceNamespace names,
        Guid afterPostId,
        CancellationToken ct = default)
    {
        var posts = await db.Posts.Find(Builders<Post>.Filter.Gt(x => x.Id, afterPostId)).SortBy(x => x.Id)
            .Limit(ReconciliationBatchSize).ToListAsync(ct);
        if (posts.Count == 0) return;

        var token = await AcquireLaneAsync(db, ct);
        var claimedIds = posts.Select(post => post.Id).ToArray();
        var versions = posts.ToDictionary(post => post.Id, post => Math.Max(1, post.IndexVersion));
        try
        {
            await EnsureConfiguredIndexAsync(db, client, names, token, ct);
            var upserts = posts.Select(post => new UpdateOneModel<SearchIndexState>(
                Builders<SearchIndexState>.Filter.Eq(row => row.PostId, post.Id),
                Builders<SearchIndexState>.Update.SetOnInsert(row => row.PostId, post.Id)
                    .SetOnInsert(row => row.AppliedVersion, 0).Max(row => row.RequestedVersion, versions[post.Id])) { IsUpsert = true }).ToArray();
            if (upserts.Length > 0) await db.SearchIndexStates.BulkWriteAsync(upserts, new BulkWriteOptions { IsOrdered = false }, ct);
            var leaseNow = DateTime.UtcNow;
            var leaseWrites = posts.Select(post => new UpdateOneModel<SearchIndexState>(
                Builders<SearchIndexState>.Filter.Eq(row => row.PostId, post.Id),
                Builders<SearchIndexState>.Update.Set(row => row.LeaseToken, token).Set(row => row.LockedUntil, leaseNow + PostLease))).ToArray();
            if (leaseWrites.Length > 0) await db.SearchIndexStates.BulkWriteAsync(leaseWrites, new BulkWriteOptions { IsOrdered = false }, ct);

            await CompleteBatchPendingTasksAsync(db, client, claimedIds, token, ct);
            await AssertLaneAsync(db, token, ct);

            var authors = posts.Select(post => post.AuthorId).Distinct().ToArray();
            var authorRows = authors.Length == 0 ? [] : await db.Users.Find(x => authors.Contains(x.Id))
                .Project(x => new { x.Id, x.Name }).ToListAsync(ct);
            var authorNames = authorRows.ToDictionary(row => row.Id, row => row.Name);
            var activePosts = posts.Where(post => post.ArchivedAt == null).ToArray();
            var activeIds = activePosts.Select(post => post.Id).ToArray();
            var topicRows = activeIds.Length == 0 ? [] : await db.PostTopics.Find(x => activeIds.Contains(x.PostId)).ToListAsync(ct);
            var topicsByPost = topicRows.GroupBy(row => row.PostId)
                .ToDictionary(group => group.Key, group => group.Select(row => row.Topic).ToArray());
            var docs = activePosts.Select(post => ToDocument(post, authorNames.GetValueOrDefault(post.AuthorId, "Deleted user"), topicsByPost.GetValueOrDefault(post.Id, []))).ToArray();
            var archivedIds = posts.Where(post => post.ArchivedAt is not null).Select(post => post.Id).ToArray();
            if (docs.Length > 0)
                await SubmitBatchAsync(docs.Select(document => Guid.Parse(document.Id)).ToArray(), docs);
            if (archivedIds.Length > 0)
                await SubmitDeleteBatchAsync(archivedIds);

            await QueueNewerVersionsAsync(db, jobs, claimedIds, ct);
            if (posts.Count == ReconciliationBatchSize)
                await EnqueueContinuationAsync(db, jobs, posts[^1].Id, ct);
        }
        finally
        {
            if (claimedIds.Length > 0)
            {
                var releaseRows = claimedIds.Select(id => new UpdateOneModel<SearchIndexState>(
                    Builders<SearchIndexState>.Filter.Eq(row => row.PostId, id) & Builders<SearchIndexState>.Filter.Eq(row => row.LeaseToken, token),
                    Builders<SearchIndexState>.Update.Set(row => row.LeaseToken, null).Set(row => row.LockedUntil, null))).ToArray();
                await db.SearchIndexStates.BulkWriteAsync(releaseRows, new BulkWriteOptions { IsOrdered = false }, CancellationToken.None);
            }
            await ReleaseLaneAsync(db, token);
        }

        async Task SubmitBatchAsync(Guid[] postIds, SearchPostDocument[] documents)
        {
            await AssertLaneAsync(db, token, ct);
            var task = await client.EnqueueUpsertAsync(Index(names), documents, cancellationToken: ct);
            await PersistBatchTaskAsync(db, postIds, token, task.Value, versions, ct);
            var rows = await db.SearchIndexStates.Find(x => postIds.Contains(x.PostId) && x.TaskId == task.Value && x.LeaseToken == token).ToListAsync(ct);
            await CompleteBatchTaskAsync(db, client, rows, token, ct);
        }

        async Task SubmitDeleteBatchAsync(Guid[] postIds)
        {
            await AssertLaneAsync(db, token, ct);
            var documentIds = postIds.Select(id => id.ToString()).ToArray();
            var task = await client.EnqueueDeleteBatchAsync(Index(names), documentIds, ct);
            await PersistBatchTaskAsync(db, postIds, token, task.Value, versions, ct);
            var rows = await db.SearchIndexStates.Find(x => postIds.Contains(x.PostId) && x.TaskId == task.Value && x.LeaseToken == token).ToListAsync(ct);
            await CompleteBatchTaskAsync(db, client, rows, token, ct);
        }
    }

    static async Task EnsureConfiguredIndexAsync(AppDb db, MeilisearchTaskClient client, OptionalServiceNamespace names, string token, CancellationToken ct)
    {
        await EnsureSearchStateAsync(db, SetupPostId, ct);
        while (true)
        {
            await AssertLaneAsync(db, token, ct);
            var state = await db.SearchIndexStates.Find(x => x.PostId == SetupPostId).FirstAsync(ct);
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
                    await db.SearchIndexStates.UpdateOneAsync(x => x.PostId == SetupPostId && x.LeaseToken == token && x.TaskId == pending,
                        Builders<SearchIndexState>.Update.Set(x => x.TaskId, null).Set(x => x.TaskVersion, null), cancellationToken: ct);
                    throw;
                }
                await db.SearchIndexStates.UpdateOneAsync(x => x.PostId == SetupPostId && x.LeaseToken == token && x.TaskId == pending,
                    Builders<SearchIndexState>.Update.Max(x => x.AppliedVersion, stage).Set(x => x.TaskId, null).Set(x => x.TaskVersion, null), cancellationToken: ct);
                continue;
            }

            if (state.AppliedVersion < 1)
            {
                var task = await client.EnsureIndexAsync(Index(names), "id", ct);
                if (task is { } create)
                {
                    await db.SearchIndexStates.UpdateOneAsync(x => x.PostId == SetupPostId && x.LeaseToken == token,
                        Builders<SearchIndexState>.Update.Set(x => x.TaskId, create.Value).Set(x => x.TaskVersion, 1), cancellationToken: ct);
                    continue;
                }
                await db.SearchIndexStates.UpdateOneAsync(x => x.PostId == SetupPostId && x.LeaseToken == token,
                    Builders<SearchIndexState>.Update.Max(x => x.AppliedVersion, 1), cancellationToken: ct);
                continue;
            }
            if (state.AppliedVersion < 2)
            {
                var task = await client.EnqueueSearchSettingsAsync(Index(names), ct);
                await db.SearchIndexStates.UpdateOneAsync(x => x.PostId == SetupPostId && x.LeaseToken == token,
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
            await db.SearchIndexStates.UpdateOneAsync(x => x.PostId == state.PostId && x.TaskId == taskId && x.LeaseToken == token,
                Builders<SearchIndexState>.Update.Set(x => x.TaskId, null).Set(x => x.TaskVersion, null), cancellationToken: ct);
            throw;
        }
        var update = Builders<SearchIndexState>.Update.Set(x => x.TaskId, null).Set(x => x.TaskVersion, null);
        if (state.TaskVersion is { } version) update = Builders<SearchIndexState>.Update.Max(x => x.AppliedVersion, version).Set(x => x.TaskId, null).Set(x => x.TaskVersion, null);
        await db.SearchIndexStates.UpdateOneAsync(x => x.PostId == state.PostId && x.TaskId == taskId && x.LeaseToken == token,
            update, cancellationToken: ct);
    }

    static async Task CompleteBatchPendingTasksAsync(AppDb db, MeilisearchTaskClient client, Guid[] ids, string token, CancellationToken ct)
    {
        var states = await db.SearchIndexStates.Find(x => ids.Contains(x.PostId) && x.LeaseToken == token && x.TaskId != null).ToListAsync(ct);
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
                Builders<SearchIndexState>.Filter.Eq(x => x.PostId, row.PostId) & Builders<SearchIndexState>.Filter.Eq(x => x.TaskId, taskId) & Builders<SearchIndexState>.Filter.Eq(x => x.LeaseToken, token),
                Builders<SearchIndexState>.Update.Set(x => x.TaskId, null).Set(x => x.TaskVersion, null))).ToArray();
            await db.SearchIndexStates.BulkWriteAsync(clear, new BulkWriteOptions { IsOrdered = false }, ct);
            throw;
        }
        var updates = states.Select(row =>
        {
            var update = Builders<SearchIndexState>.Update.Set(x => x.TaskId, null).Set(x => x.TaskVersion, null);
            if (row.TaskVersion is { } version) update = Builders<SearchIndexState>.Update.Max(x => x.AppliedVersion, version).Set(x => x.TaskId, null).Set(x => x.TaskVersion, null);
            return new UpdateOneModel<SearchIndexState>(
                Builders<SearchIndexState>.Filter.Eq(x => x.PostId, row.PostId) & Builders<SearchIndexState>.Filter.Eq(x => x.TaskId, taskId) & Builders<SearchIndexState>.Filter.Eq(x => x.LeaseToken, token), update);
        }).ToArray();
        await db.SearchIndexStates.BulkWriteAsync(updates, new BulkWriteOptions { IsOrdered = false }, ct);
    }

    static async Task SaveTaskAsync(AppDb db, Guid postId, string token, long taskId, long version, CancellationToken ct)
    {
        var result = await db.SearchIndexStates.UpdateOneAsync(x => x.PostId == postId && x.LeaseToken == token,
            Builders<SearchIndexState>.Update.Set(x => x.TaskId, taskId).Set(x => x.TaskVersion, version), cancellationToken: ct);
        if (result.ModifiedCount == 0) throw new InvalidOperationException("Search post lease was lost before task persistence.");
    }

    static async Task PersistBatchTaskAsync(AppDb db, Guid[] postIds, string token, long taskId, IReadOnlyDictionary<Guid, long> versions, CancellationToken ct)
    {
        var updates = postIds.Select(id => new UpdateOneModel<SearchIndexState>(
            Builders<SearchIndexState>.Filter.Eq(x => x.PostId, id) & Builders<SearchIndexState>.Filter.Eq(x => x.LeaseToken, token),
            Builders<SearchIndexState>.Update.Set(x => x.TaskId, taskId).Set(x => x.TaskVersion, versions[id]))).ToArray();
        if (updates.Length > 0) await db.SearchIndexStates.BulkWriteAsync(updates, new BulkWriteOptions { IsOrdered = false }, ct);
    }

    static async Task QueueNewerVersionsAsync(AppDb db, JobQueue jobs, Guid[] ids, CancellationToken ct)
    {
        var states = await db.SearchIndexStates.Find(x => ids.Contains(x.PostId) && x.RequestedVersion > x.AppliedVersion).Project(x => x.PostId).ToListAsync(ct);
        if (states.Count > 0) await EnqueueBatchAsync(db, jobs, null, ct);
    }

    static async Task EnqueueContinuationAsync(AppDb db, JobQueue jobs, Guid afterPostId, CancellationToken ct)
    {
        var payloads = await db.Jobs.Find(x => x.Type == nameof(ReindexSearchBatch)
                && (x.Status == JobStatus.Pending || x.Status == JobStatus.Running))
            .Project(x => x.Payload).ToListAsync(ct);
        foreach (var payload in payloads)
        {
            try
            {
                if (JsonSerializer.Deserialize<ReindexSearchBatch>(payload, JobJson) is { } job && job.AfterPostId == afterPostId) return;
            }
            catch (JsonException) { }
        }
        await jobs.EnqueueAsync(db, new ReindexSearchBatch(afterPostId), ct: ct);
        jobs.Wake();
    }

    static SearchPostDocument ToDocument(Post post, string authorName, IReadOnlyCollection<string> topics) => new(
        post.Id.ToString(), post.Title, post.Body, post.Excerpt, post.Slug,
        post.AuthorId, authorName, topics.ToArray(), DateTime.SpecifyKind(post.PublishedAt, DateTimeKind.Utc));

    static async Task UpsertRequestedVersionAsync(AppDb db, Guid postId, long version, CancellationToken ct)
    {
        var update = Builders<SearchIndexState>.Update.SetOnInsert(x => x.PostId, postId)
            .SetOnInsert(x => x.AppliedVersion, 0).Max(x => x.RequestedVersion, version);
        try { await db.SearchIndexStates.UpdateOneAsync(x => x.PostId == postId, update, new UpdateOptions { IsUpsert = true }, ct); }
        catch (MongoWriteException error) when (error.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        { await db.SearchIndexStates.UpdateOneAsync(x => x.PostId == postId, Builders<SearchIndexState>.Update.Max(x => x.RequestedVersion, version), cancellationToken: ct); }
    }

    static async Task EnsureSearchStateAsync(AppDb db, Guid postId, CancellationToken ct)
    {
        try
        {
            await db.SearchIndexStates.UpdateOneAsync(x => x.PostId == postId,
                Builders<SearchIndexState>.Update.SetOnInsert(x => x.PostId, postId).SetOnInsert(x => x.AppliedVersion, 0).SetOnInsert(x => x.RequestedVersion, 0),
                new UpdateOptions { IsUpsert = true }, ct);
        }
        catch (MongoWriteException error) when (error.WriteError?.Category == ServerErrorCategory.DuplicateKey) { }
    }

    static async Task<string> AcquireLaneAsync(AppDb db, CancellationToken ct)
    {
        await EnsureSearchStateAsync(db, SetupPostId, ct);
        var token = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var lane = await db.SearchIndexStates.FindOneAndUpdateAsync(
            x => x.PostId == SetupPostId && (x.LockedUntil == null || x.LockedUntil < now),
            Builders<SearchIndexState>.Update.Set(x => x.LeaseToken, token).Set(x => x.LockedUntil, now + LaneLease),
            new FindOneAndUpdateOptions<SearchIndexState> { ReturnDocument = ReturnDocument.After }, ct);
        if (lane is null) throw new InvalidOperationException($"The {LaneName} lease is held; retry later.");
        return token;
    }

    static async Task AssertLaneAsync(AppDb db, string token, CancellationToken ct)
    {
        if (!await db.SearchIndexStates.Find(x => x.PostId == SetupPostId && x.LeaseToken == token && x.LockedUntil > DateTime.UtcNow).AnyAsync(ct))
            throw new InvalidOperationException("The global search indexing lease was lost.");
    }

    static Task ReleaseLaneAsync(AppDb db, string token) => db.SearchIndexStates.UpdateOneAsync(
        x => x.PostId == SetupPostId && x.LeaseToken == token,
        Builders<SearchIndexState>.Update.Set(x => x.LeaseToken, null).Set(x => x.LockedUntil, null),
        cancellationToken: CancellationToken.None);

    static void ValidateRequest(Guid postId, long version)
    {
        if (postId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(postId));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
    }
}
