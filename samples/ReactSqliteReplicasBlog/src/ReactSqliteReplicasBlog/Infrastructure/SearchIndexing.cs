using ReactSqliteReplicasBlog.Data;
using ReactSqliteReplicasBlog.Jobs;
using Microsoft.EntityFrameworkCore;

namespace ReactSqliteReplicasBlog.Infrastructure;

public sealed record SyncSearchPost(Guid PostId);
public sealed record SyncSearchPostsBatch();
public sealed record ReindexSearchBatch(Guid AfterPostId = default);
public sealed record SearchPostDocument(string Id, string Title, string Body, string AuthorId, string AuthorName, string[] Topics, DateTime PublishedAt);

/// <summary>Durable per-post index intent; Meilisearch task IDs stay in the database across restarts.</summary>
public static class SearchIndexing
{
    public const int ReconciliationBatchSize = 500;
    static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    static readonly TimeSpan LaneDuration = TimeSpan.FromMinutes(10);

    public static async Task RequestAsync(AppDb db, JobQueue jobs, Guid postId, long requestedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(jobs);
        if (postId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(postId));
        if (requestedVersion < 1) throw new ArgumentOutOfRangeException(nameof(requestedVersion));
        await db.SaveChangesAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "SearchIndexStates" ("PostId", "RequestedVersion", "AppliedVersion", "TaskId", "LeaseToken", "LockedUntil")
            VALUES ({{postId}}, {{requestedVersion}}, 0, NULL, NULL, NULL)
            ON CONFLICT("PostId") DO UPDATE SET "RequestedVersion" = MAX("SearchIndexStates"."RequestedVersion", excluded."RequestedVersion")
            """, cancellationToken);
        await EnqueueBatchAsync(db, jobs, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Queues one bulk sync job for a burst of mutations. A pending or running batch already
    /// covers every requested version, so duplicate jobs are coalesced instead of queued per post.
    /// </summary>
    public static async Task<bool> EnqueueBatchAsync(AppDb db, JobQueue jobs, CancellationToken cancellationToken = default)
    {
        if (await db.Jobs.AnyAsync(job => job.Type == nameof(SyncSearchPostsBatch)
                && (job.Status == JobStatus.Pending || job.Status == JobStatus.Running), cancellationToken)) return false;
        jobs.Enqueue(db, new SyncSearchPostsBatch());
        return true;
    }

    /// <summary>Queues outstanding seed/recovery work as bulk batches instead of one job per post.</summary>
    public static async Task<int> ReconcileAsync(AppDb db, JobQueue jobs, CancellationToken cancellationToken = default, int batchSize = ReconciliationBatchSize)
    {
        if (batchSize is < 1 or > ReconciliationBatchSize) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var missing = await db.Posts.AsNoTracking()
            .Where(post => !db.SearchIndexStates.Any(state => state.PostId == post.Id))
            .OrderBy(post => post.Id).Select(post => new { post.Id, post.IndexVersion }).Take(batchSize).ToListAsync(cancellationToken);
        foreach (var post in missing) await RequestAsync(db, jobs, post.Id, post.IndexVersion, cancellationToken);
        if (missing.Count > 0) await db.SaveChangesAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var hasDue = await db.SearchIndexStates.AsNoTracking().AnyAsync(row => row.PostId != Guid.Empty
            && row.RequestedVersion > row.AppliedVersion && (row.LockedUntil == null || row.LockedUntil < now), cancellationToken);
        if (!hasDue) return missing.Count;
        await EnqueueBatchAsync(db, jobs, cancellationToken);
        return missing.Count + 1;
    }

    public static async Task ProcessAsync(AppDb db, MeilisearchTaskClient client, OptionalServiceNamespace serviceNamespace,
        Guid postId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(serviceNamespace);
        if (postId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(postId));
        var token = Guid.NewGuid().ToString("N");
        var laneToken = Guid.NewGuid().ToString("N");
        DateTime laneUntil;
        while (true)
        {
            var now = DateTime.UtcNow;
            laneUntil = now + LaneDuration;
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT OR IGNORE INTO "SearchIndexStates" ("PostId", "RequestedVersion", "AppliedVersion", "TaskId", "LeaseToken", "LockedUntil")
                VALUES ({{Guid.Empty}}, 0, 0, NULL, NULL, NULL)
                """, cancellationToken);
            var laneClaimed = await db.SearchIndexStates.Where(row => row.PostId == Guid.Empty && (row.LockedUntil == null || row.LockedUntil < now))
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseToken, laneToken).SetProperty(row => row.LockedUntil, laneUntil), cancellationToken);
            if (laneClaimed == 1) break;
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        try
        {
            var now = DateTime.UtcNow;
            var claimed = await db.SearchIndexStates.Where(row => row.PostId == postId && row.RequestedVersion > row.AppliedVersion
                    && (row.LockedUntil == null || row.LockedUntil < now))
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseToken, token).SetProperty(row => row.LockedUntil, now + LeaseDuration), cancellationToken);
            if (claimed == 0) return;
            var indexUid = serviceNamespace.MeilisearchIndex("posts");
            await EnsureConfiguredIndexAsync(db, client, indexUid, cancellationToken);

            while (true)
            {
                var state = await db.SearchIndexStates.AsNoTracking().SingleAsync(row => row.PostId == postId, cancellationToken);
                if (state.LeaseToken != token) return;
                if (state.TaskId is { } pending)
                {
                    var indexedVersion = state.TaskVersion;
                    try { await client.WaitForSuccessAsync(new MeilisearchTaskId(pending), cancellationToken: cancellationToken); }
                    catch (MeilisearchTaskFailedException)
                    {
                        await db.SearchIndexStates.Where(row => row.PostId == postId && row.LeaseToken == token)
                            .ExecuteUpdateAsync(set => set.SetProperty(row => row.TaskId, (long?)null).SetProperty(row => row.TaskVersion, (long?)null), cancellationToken);
                        throw;
                    }
                    var pendingCompletion = db.SearchIndexStates.Where(row => row.PostId == postId && row.LeaseToken == token);
                    if (indexedVersion is { } completedVersion)
                        await pendingCompletion.ExecuteUpdateAsync(set => set.SetProperty(row => row.AppliedVersion, completedVersion)
                            .SetProperty(row => row.TaskId, (long?)null).SetProperty(row => row.TaskVersion, (long?)null), cancellationToken);
                    else
                        await pendingCompletion.ExecuteUpdateAsync(set => set.SetProperty(row => row.TaskId, (long?)null)
                            .SetProperty(row => row.TaskVersion, (long?)null), cancellationToken);
                    continue;
                }

                var version = state.RequestedVersion;
                var post = await db.Posts.IgnoreQueryFilters().AsNoTracking().Where(row => row.Id == postId
                        && row.ArchivedAt == null && row.IsPublished)
                    .Select(row => new { row.Id, row.Title, row.Body, row.AuthorId, AuthorName = row.Author.DisplayName, row.PublishedAt })
                    .SingleOrDefaultAsync(cancellationToken);
                MeilisearchTaskId task;
                if (post is null)
                    task = await client.EnqueueDeleteAsync(indexUid, postId.ToString(), cancellationToken);
                else
                {
                    var topics = await db.PostTopics.AsNoTracking().Where(row => row.PostId == postId).Select(row => row.Topic).ToArrayAsync(cancellationToken);
                    var document = new SearchPostDocument(post.Id.ToString(), post.Title, post.Body, post.AuthorId.ToString(), post.AuthorName, topics, post.PublishedAt);
                    task = await client.EnqueueUpsertAsync(indexUid, new[] { document }, cancellationToken: cancellationToken);
                }
                var saved = await db.SearchIndexStates.Where(row => row.PostId == postId && row.LeaseToken == token)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.TaskId, (long?)task.Value)
                        .SetProperty(row => row.TaskVersion, (long?)version), cancellationToken);
                if (saved == 0) return;
                await client.WaitForSuccessAsync(task, cancellationToken: cancellationToken);
                var completed = await db.SearchIndexStates.Where(row => row.PostId == postId && row.LeaseToken == token)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.AppliedVersion, version)
                        .SetProperty(row => row.TaskId, (long?)null).SetProperty(row => row.TaskVersion, (long?)null), cancellationToken);
                if (completed == 0) return;
                var latest = await db.SearchIndexStates.AsNoTracking().SingleAsync(row => row.PostId == postId, cancellationToken);
                if (latest.RequestedVersion <= version) return;
            }
        }
        finally
        {
            await db.SearchIndexStates.Where(row => row.PostId == postId && row.LeaseToken == token)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseToken, (string?)null).SetProperty(row => row.LockedUntil, (DateTime?)null), CancellationToken.None);
            await db.SearchIndexStates.Where(row => row.PostId == Guid.Empty && row.LeaseToken == laneToken && row.LockedUntil == laneUntil)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseToken, (string?)null).SetProperty(row => row.LockedUntil, (DateTime?)null), CancellationToken.None);
        }
    }

    /// <summary>
    /// Drains outstanding index intents in one Meilisearch bulk request (up to 500 posts).
    /// Normal mutations queue this single coalesced job instead of one job per post.
    /// </summary>
    public static async Task ProcessBatchAsync(AppDb db, JobQueue jobs, MeilisearchTaskClient client,
        OptionalServiceNamespace serviceNamespace, CancellationToken ct = default)
    {
        var lease = Guid.NewGuid().ToString("N");
        var laneToken = Guid.NewGuid().ToString("N");
        DateTime laneUntil;
        while (true)
        {
            var now = DateTime.UtcNow;
            laneUntil = now + LaneDuration;
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT OR IGNORE INTO "SearchIndexStates" ("PostId", "RequestedVersion", "AppliedVersion", "TaskId", "TaskVersion", "LeaseToken", "LockedUntil")
                VALUES ({{Guid.Empty}}, 0, 0, NULL, NULL, NULL, NULL)
                """, ct);
            if (await db.SearchIndexStates.Where(row => row.PostId == Guid.Empty && (row.LockedUntil == null || row.LockedUntil < now))
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseToken, laneToken).SetProperty(row => row.LockedUntil, laneUntil), ct) == 1)
                break;
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }

        try
        {
            var indexUid = serviceNamespace.MeilisearchIndex("posts");
            await EnsureConfiguredIndexAsync(db, client, indexUid, ct);

            var now = DateTime.UtcNow;
            var ids = await db.SearchIndexStates.AsNoTracking()
                .Where(row => row.PostId != Guid.Empty && row.RequestedVersion > row.AppliedVersion && (row.LockedUntil == null || row.LockedUntil < now))
                .OrderBy(row => row.PostId).Select(row => row.PostId).Take(ReconciliationBatchSize).ToListAsync(ct);
            if (ids.Count == 0) return;

            var states = await db.SearchIndexStates.Where(row => ids.Contains(row.PostId)).ToListAsync(ct);

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

            var claimed = states.Where(row => row.PostId != Guid.Empty && row.RequestedVersion > row.AppliedVersion
                && (row.LockedUntil == null || row.LockedUntil < now)).ToList();
            if (claimed.Count == 0) { await db.SaveChangesAsync(ct); return; }
            foreach (var state in claimed) { state.LeaseToken = lease; state.LockedUntil = now + LeaseDuration; }
            await db.SaveChangesAsync(ct);

            var claimedIds = claimed.Select(row => row.PostId).ToArray();
            var posts = await db.Posts.IgnoreQueryFilters().AsNoTracking().Where(post => claimedIds.Contains(post.Id))
                .Select(post => new { post.Id, post.Title, post.Body, post.AuthorId, AuthorName = post.Author.DisplayName,
                    post.PublishedAt, post.IsPublished, post.ArchivedAt, post.IndexVersion })
                .ToListAsync(ct);

            var upsertPosts = posts.Where(post => post.IsPublished && post.ArchivedAt == null).ToArray();
            var upsertIds = upsertPosts.Select(post => post.Id).ToArray();
            var topicRows = await db.PostTopics.AsNoTracking()
                .Where(topic => upsertIds.Contains(topic.PostId))
                .Select(topic => new { topic.PostId, topic.Topic }).ToListAsync(ct);
            var topics = topicRows.GroupBy(row => row.PostId).ToDictionary(group => group.Key, group => group.Select(row => row.Topic).ToArray());

            if (upsertPosts.Length > 0)
            {
                var documents = upsertPosts.Select(post => new SearchPostDocument(post.Id.ToString(),
                    post.Title, post.Body, post.AuthorId.ToString(), post.AuthorName, topics.GetValueOrDefault(post.Id, []), post.PublishedAt)).ToArray();
                await SubmitAsync(upsertIds, documents);
            }

            // Posts that no longer exist (deleted) are removed from the index too.
            var missingIds = claimedIds.Except(posts.Select(post => post.Id)).ToArray();
            var deleteIds = posts.Where(post => !post.IsPublished || post.ArchivedAt != null).Select(post => post.Id).Concat(missingIds).ToArray();
            if (deleteIds.Length > 0) await SubmitAsync(deleteIds, null);

            foreach (var state in claimed) { state.LeaseToken = null; state.LockedUntil = null; }
            await db.SaveChangesAsync(ct);

            // Drain the next batch promptly while unlocked work remains.
            if (await db.SearchIndexStates.AnyAsync(row => row.PostId != Guid.Empty && row.RequestedVersion > row.AppliedVersion
                    && (row.LockedUntil == null || row.LockedUntil < DateTime.UtcNow), ct))
            {
                jobs.Enqueue(db, new SyncSearchPostsBatch());
                await db.SaveChangesAsync(ct);
                jobs.Wake();
            }

            async Task SubmitAsync(Guid[] batchIds, SearchPostDocument[]? documents)
            {
                var task = documents is { Length: > 0 }
                    ? await client.EnqueueUpsertAsync(indexUid, documents, cancellationToken: ct)
                    : await client.EnqueueDeleteBatchAsync(indexUid, batchIds.Select(id => id.ToString()).ToArray(), ct);
                var batchStates = claimed.Where(state => batchIds.Contains(state.PostId)).ToArray();
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
            await db.SearchIndexStates.Where(row => row.PostId == Guid.Empty && row.LeaseToken == laneToken && row.LockedUntil == laneUntil)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseToken, (string?)null).SetProperty(row => row.LockedUntil, (DateTime?)null), CancellationToken.None);
        }
    }

    /// <summary>Starts one durable cursor chain. Each job indexes at most 500 posts.</summary>
    public static async Task<bool> RequestFullReindexAsync(AppDb db, JobQueue jobs, CancellationToken ct = default)
    {
        if (await db.Jobs.AnyAsync(job => job.Type == nameof(ReindexSearchBatch)
                && (job.Status == JobStatus.Pending || job.Status == JobStatus.Running), ct)) return false;
        jobs.Enqueue(db, new ReindexSearchBatch());
        await db.SaveChangesAsync(ct);
        jobs.Wake();
        return true;
    }

    public static async Task ProcessReindexBatchAsync(AppDb db, JobQueue jobs, MeilisearchTaskClient client,
        OptionalServiceNamespace serviceNamespace, Guid afterPostId, CancellationToken ct = default)
    {
        var laneToken = Guid.NewGuid().ToString("N");
        DateTime laneUntil;
        while (true)
        {
            var now = DateTime.UtcNow;
            laneUntil = now + LaneDuration;
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT OR IGNORE INTO "SearchIndexStates" ("PostId", "RequestedVersion", "AppliedVersion", "TaskId", "TaskVersion", "LeaseToken", "LockedUntil")
                VALUES ({{Guid.Empty}}, 0, 0, NULL, NULL, NULL, NULL)
                """, ct);
            if (await db.SearchIndexStates.Where(row => row.PostId == Guid.Empty && (row.LockedUntil == null || row.LockedUntil < now))
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseToken, laneToken).SetProperty(row => row.LockedUntil, laneUntil), ct) == 1)
                break;
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }

        try
        {
            var indexUid = serviceNamespace.MeilisearchIndex("posts");
            await EnsureConfiguredIndexAsync(db, client, indexUid, ct);
            var ids = await db.Posts.IgnoreQueryFilters().AsNoTracking().Where(post => post.Id > afterPostId)
                .OrderBy(post => post.Id).Select(post => post.Id).Take(ReconciliationBatchSize).ToListAsync(ct);
            if (ids.Count == 0) return;

            var posts = await db.Posts.IgnoreQueryFilters().AsNoTracking().Where(post => ids.Contains(post.Id))
                .Select(post => new { post.Id, post.Title, post.Body, post.AuthorId, AuthorName = post.Author.DisplayName,
                    post.PublishedAt, post.IsPublished, post.ArchivedAt, post.IndexVersion })
                .ToListAsync(ct);
            var states = await db.SearchIndexStates.Where(state => ids.Contains(state.PostId)).ToListAsync(ct);
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

            var byId = states.ToDictionary(state => state.PostId);
            var postById = posts.ToDictionary(post => post.Id);
            foreach (var id in ids)
            {
                if (!byId.TryGetValue(id, out var state))
                {
                    state = new SearchIndexState { PostId = id, AppliedVersion = 0, RequestedVersion = Math.Max(postById[id].IndexVersion, 1) };
                    db.SearchIndexStates.Add(state);
                    byId.Add(id, state);
                }
            }
            await db.SaveChangesAsync(ct);

            var upsertPosts = posts.Where(post => post.IsPublished && post.ArchivedAt == null).ToArray();
            var upsertIds = upsertPosts.Select(post => post.Id).ToArray();
            var topicRows = await db.PostTopics.AsNoTracking()
                .Where(topic => upsertIds.Contains(topic.PostId))
                .Select(topic => new { topic.PostId, topic.Topic }).ToListAsync(ct);
            var topics = topicRows.GroupBy(row => row.PostId).ToDictionary(group => group.Key, group => group.Select(row => row.Topic).ToArray());
            if (upsertPosts.Length > 0)
            {
                var documents = upsertPosts.Select(post => new SearchPostDocument(post.Id.ToString(),
                    post.Title, post.Body, post.AuthorId.ToString(), post.AuthorName, topics.GetValueOrDefault(post.Id, []), post.PublishedAt)).ToArray();
                await SubmitBatchAsync(upsertIds, documents);
            }
            var deleteIds = posts.Where(post => !post.IsPublished || post.ArchivedAt != null).Select(post => post.Id).ToArray();
            if (deleteIds.Length > 0)
                await SubmitBatchAsync(deleteIds, null);

            if (ids.Count == ReconciliationBatchSize)
                jobs.Enqueue(db, new ReindexSearchBatch(ids[^1]));
            await db.SaveChangesAsync(ct);
            jobs.Wake();

            async Task SubmitBatchAsync(Guid[] batchIds, SearchPostDocument[]? documents)
            {
                var task = documents is { Length: > 0 }
                    ? await client.EnqueueUpsertAsync(indexUid, documents, cancellationToken: ct)
                    : await client.EnqueueDeleteBatchAsync(indexUid, batchIds.Select(id => id.ToString()).ToArray(), ct);
                var batchStates = batchIds.Select(id => byId[id]).ToArray();
                foreach (var state in batchStates) { state.TaskId = task.Value; state.TaskVersion = postById[state.PostId].IndexVersion; }
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
            await db.SearchIndexStates.Where(row => row.PostId == Guid.Empty && row.LeaseToken == laneToken && row.LockedUntil == laneUntil)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseToken, (string?)null).SetProperty(row => row.LockedUntil, (DateTime?)null), CancellationToken.None);
        }
    }

    static async Task EnsureConfiguredIndexAsync(AppDb db, MeilisearchTaskClient client, string indexUid, CancellationToken ct)
    {
        while (true)
        {
            var setup = await db.SearchIndexStates.AsNoTracking().SingleAsync(row => row.PostId == Guid.Empty, ct);
            if (setup.TaskId is { } pending)
            {
                var stage = setup.TaskVersion ?? 1;
                try
                {
                    if (stage == 1) await client.ConfirmIndexCreationAsync(new MeilisearchTaskId(pending), indexUid, ct);
                    else await client.WaitForSuccessAsync(new MeilisearchTaskId(pending), cancellationToken: ct);
                }
                catch (MeilisearchTaskFailedException)
                {
                    await db.SearchIndexStates.Where(row => row.PostId == Guid.Empty)
                        .ExecuteUpdateAsync(set => set.SetProperty(row => row.TaskId, (long?)null)
                            .SetProperty(row => row.TaskVersion, (long?)null), ct);
                    throw;
                }
                await db.SearchIndexStates.Where(row => row.PostId == Guid.Empty && row.TaskId == pending)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.AppliedVersion, stage)
                        .SetProperty(row => row.TaskId, (long?)null).SetProperty(row => row.TaskVersion, (long?)null), ct);
                continue;
            }

            if (setup.AppliedVersion < 1)
            {
                var create = await client.EnsureIndexAsync(indexUid, "id", ct);
                if (create is { } createTask)
                {
                    await db.SearchIndexStates.Where(row => row.PostId == Guid.Empty)
                        .ExecuteUpdateAsync(set => set.SetProperty(row => row.TaskId, (long?)createTask.Value)
                            .SetProperty(row => row.TaskVersion, (long?)1), ct);
                    continue;
                }
                await db.SearchIndexStates.Where(row => row.PostId == Guid.Empty)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.AppliedVersion, 1L), ct);
                continue;
            }

            if (setup.AppliedVersion < 2)
            {
                var settings = await client.EnqueueSearchSettingsAsync(indexUid, ct);
                await db.SearchIndexStates.Where(row => row.PostId == Guid.Empty)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.TaskId, (long?)settings.Value)
                        .SetProperty(row => row.TaskVersion, (long?)2), ct);
                continue;
            }
            return;
        }
    }
}

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
