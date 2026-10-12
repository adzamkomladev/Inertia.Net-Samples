using Microsoft.EntityFrameworkCore;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Features.Search;

/// <summary>Durable queue item for reconciling one post's search document.</summary>
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
        Guid postId,
        long requestedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(jobs);
        if (postId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(postId));
        if (requestedVersion < 1) throw new ArgumentOutOfRangeException(nameof(requestedVersion));

        // The post mutation must be durable before its search state can be processed. If the caller
        // owns a transaction, both writes remain in that transaction.
        await db.SaveChangesAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "SearchIndexStates" ("PostId", "RequestedVersion", "AppliedVersion", "TaskId", "LeaseToken", "LockedUntil")
            VALUES ({{postId}}, {{requestedVersion}}, 0, NULL, NULL, NULL)
            ON CONFLICT("PostId") DO UPDATE SET
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

        // Backfill existing posts when search is enabled after the database was created.
        var missing = await db.Posts.AsNoTracking()
            .Where(post => !db.SearchIndexStates.Any(state => state.PostId == post.Id))
            .OrderBy(post => post.Id).Select(post => new { post.Id, post.IndexVersion })
            .Take(batchSize).ToListAsync(cancellationToken);
        foreach (var post in missing)
            await RequestAsync(db, jobs, post.Id, post.IndexVersion, cancellationToken);

        var now = DateTime.UtcNow;
        if (!await db.SearchIndexStates.AnyAsync(state => state.PostId != Guid.Empty && state.RequestedVersion > state.AppliedVersion
                && (state.LockedUntil == null || state.LockedUntil < now), cancellationToken)) return missing.Count;
        return missing.Count + (await EnqueueBatchAsync(db, jobs, cancellationToken) ? 1 : 0);
    }

    public static async Task<bool> EnqueueBatchAsync(AppDb db, JobQueue jobs, CancellationToken ct = default)
    {
        if (await db.Jobs.AnyAsync(job => job.Type == nameof(SyncSearchPostsBatch)
                && (job.Status == JobStatus.Pending || job.Status == JobStatus.Running), ct)) return false;
        await jobs.EnqueueAsync(db, new SyncSearchPostsBatch(), ct: ct);
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
        Guid postId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(serviceNamespace);
        if (postId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(postId));

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
            var claimed = await db.SearchIndexStates.Where(state => state.PostId == postId
                    && state.RequestedVersion > state.AppliedVersion
                    && (state.LockedUntil == null || state.LockedUntil < now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(state => state.LeaseToken, token)
                    .SetProperty(state => state.LockedUntil, now + LeaseDuration), cancellationToken);
            if (claimed == 0) return;

            var indexUid = serviceNamespace.MeilisearchIndex("posts");
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "SearchIndexStates" ("PostId", "RequestedVersion", "AppliedVersion", "TaskId", "TaskVersion", "LeaseToken", "LockedUntil")
                VALUES ({{Guid.Empty}}, 0, 0, NULL, NULL, NULL, NULL) ON CONFLICT DO NOTHING
                """, cancellationToken);
            await EnsureConfiguredIndexAsync(db, client, indexUid, cancellationToken);

            while (true)
            {
                var state = await db.SearchIndexStates.AsNoTracking().SingleAsync(row => row.PostId == postId, cancellationToken);
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
                        await db.SearchIndexStates.Where(row => row.PostId == postId && row.LeaseToken == token)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TaskId, (long?)null)
                                .SetProperty(row => row.TaskVersion, (long?)null), cancellationToken);
                        throw;
                    }
                    var pendingCompletion = db.SearchIndexStates.Where(row => row.PostId == postId && row.LeaseToken == token);
                    if (indexedVersion is { } completedVersion)
                        await pendingCompletion.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.AppliedVersion, completedVersion)
                            .SetProperty(row => row.TaskId, (long?)null).SetProperty(row => row.TaskVersion, (long?)null), cancellationToken);
                    else
                        await pendingCompletion.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TaskId, (long?)null)
                            .SetProperty(row => row.TaskVersion, (long?)null), cancellationToken);
                    continue;
                }

                var version = state.RequestedVersion;
                var post = await db.Posts.IgnoreQueryFilters().AsNoTracking()
                    .Where(row => row.Id == postId && row.ArchivedAt == null)
                    .Select(row => new
                    {
                        row.Id,
                        row.Title,
                        row.Body,
                        row.Excerpt,
                        row.Slug,
                        row.AuthorId,
                        AuthorName = row.Author.Name,
                        row.PublishedAt,
                    })
                    .SingleOrDefaultAsync(cancellationToken);

                SearchPostDocument? document = null;
                if (post is not null)
                {
                    var topics = await db.PostTopics.AsNoTracking().Where(topic => topic.PostId == postId)
                        .Select(topic => topic.Topic).ToArrayAsync(cancellationToken);
                    document = new SearchPostDocument(
                        post.Id.ToString(), post.Title, post.Body, post.Excerpt, post.Slug,
                        post.AuthorId, post.AuthorName, topics, post.PublishedAt);
                }

                var enqueuedTask = document is null
                    ? await client.EnqueueDeleteAsync(indexUid, postId.ToString(), cancellationToken)
                    : await client.EnqueueUpsertAsync(indexUid, new[] { document }, cancellationToken: cancellationToken);
                var saved = await db.SearchIndexStates.Where(row => row.PostId == postId && row.LeaseToken == token)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TaskId, (long?)enqueuedTask.Value)
                        .SetProperty(row => row.TaskVersion, (long?)version), cancellationToken);
                if (saved == 0) return;

                await client.WaitForSuccessAsync(enqueuedTask, cancellationToken: cancellationToken);
                var completed = await db.SearchIndexStates.Where(row => row.PostId == postId && row.LeaseToken == token)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(row => row.AppliedVersion, version)
                        .SetProperty(row => row.TaskId, (long?)null)
                        .SetProperty(row => row.TaskVersion, (long?)null), cancellationToken);
                if (completed == 0) return;

                var latest = await db.SearchIndexStates.AsNoTracking().SingleAsync(row => row.PostId == postId, cancellationToken);
                if (latest.RequestedVersion <= version) return;
            }
        }
        finally
        {
            await db.SearchIndexStates.Where(state => state.PostId == postId && state.LeaseToken == token)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(state => state.LeaseToken, (string?)null)
                    .SetProperty(state => state.LockedUntil, (DateTime?)null), CancellationToken.None);
            await db.CronLeases.Where(lease => lease.Name == LaneName && lease.LastRunAt == laneLeaseAt)
                .ExecuteUpdateAsync(setters => setters.SetProperty(lease => lease.LockedUntil, (DateTime?)null), CancellationToken.None);
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
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TaskId, (long?)null)
                            .SetProperty(row => row.TaskVersion, (long?)null), ct);
                    throw;
                }
                await db.SearchIndexStates.Where(row => row.PostId == Guid.Empty && row.TaskId == pending)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.AppliedVersion, stage)
                        .SetProperty(row => row.TaskId, (long?)null).SetProperty(row => row.TaskVersion, (long?)null), ct);
                continue;
            }
            if (setup.AppliedVersion < 1)
            {
                var create = await client.EnsureIndexAsync(indexUid, "id", ct);
                if (create is { } createTask)
                {
                    await db.SearchIndexStates.Where(row => row.PostId == Guid.Empty)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TaskId, (long?)createTask.Value)
                            .SetProperty(row => row.TaskVersion, (long?)1), ct);
                    continue;
                }
                await db.SearchIndexStates.Where(row => row.PostId == Guid.Empty)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.AppliedVersion, 1L), ct);
                continue;
            }
            if (setup.AppliedVersion < 2)
            {
                var settings = await client.EnqueueSearchSettingsAsync(indexUid, ct);
                await db.SearchIndexStates.Where(row => row.PostId == Guid.Empty)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TaskId, (long?)settings.Value)
                        .SetProperty(row => row.TaskVersion, (long?)2), ct);
                continue;
            }
            return;
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
            var indexUid = serviceNamespace.MeilisearchIndex("posts");
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "SearchIndexStates" ("PostId", "RequestedVersion", "AppliedVersion", "TaskId", "TaskVersion", "LeaseToken", "LockedUntil")
                VALUES ({{Guid.Empty}}, 0, 0, NULL, NULL, NULL, NULL) ON CONFLICT ("PostId") DO NOTHING
                """, ct);
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
                .Select(post => new
                {
                    post.Id,
                    post.Title,
                    post.Body,
                    post.Excerpt,
                    post.Slug,
                    post.AuthorId,
                    AuthorName = post.Author.Name,
                    post.PublishedAt,
                    post.ArchivedAt,
                    post.IndexVersion
                })
                .ToListAsync(ct);

            var upsertPosts = posts.Where(post => post.ArchivedAt == null).ToArray();
            var upsertIds = upsertPosts.Select(post => post.Id).ToArray();
            var topicRows = await db.PostTopics.AsNoTracking()
                .Where(topic => upsertIds.Contains(topic.PostId))
                .Select(topic => new { topic.PostId, topic.Topic }).ToListAsync(ct);
            var topics = topicRows.GroupBy(row => row.PostId).ToDictionary(group => group.Key, group => group.Select(row => row.Topic).ToArray());

            if (upsertPosts.Length > 0)
            {
                var documents = upsertPosts.Select(post => new SearchPostDocument(post.Id.ToString(),
                    post.Title, post.Body, post.Excerpt, post.Slug, post.AuthorId, post.AuthorName, topics.GetValueOrDefault(post.Id, []), post.PublishedAt)).ToArray();
                await SubmitAsync(upsertIds, documents);
            }

            // Posts that no longer exist (deleted) are removed from the index too.
            var missingIds = claimedIds.Except(posts.Select(post => post.Id)).ToArray();
            var deleteIds = posts.Where(post => post.ArchivedAt != null).Select(post => post.Id).Concat(missingIds).ToArray();
            if (deleteIds.Length > 0) await SubmitAsync(deleteIds, null);

            foreach (var state in claimed) { state.LeaseToken = null; state.LockedUntil = null; }
            await db.SaveChangesAsync(ct);

            // Drain the next batch promptly while unlocked work remains.
            if (await db.SearchIndexStates.AnyAsync(row => row.PostId != Guid.Empty && row.RequestedVersion > row.AppliedVersion
                    && (row.LockedUntil == null || row.LockedUntil < DateTime.UtcNow), ct))
            {
                jobs.Enqueue(db, new SyncSearchPostsBatch());
                await db.SaveChangesAsync(ct);

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
        OptionalServiceNamespace serviceNamespace, Guid afterPostId, CancellationToken ct = default)
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
            var indexUid = serviceNamespace.MeilisearchIndex("posts");
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "SearchIndexStates" ("PostId", "RequestedVersion", "AppliedVersion", "TaskId", "TaskVersion", "LeaseToken", "LockedUntil")
                VALUES ({{Guid.Empty}}, 0, 0, NULL, NULL, NULL, NULL) ON CONFLICT ("PostId") DO NOTHING
                """, ct);
            await EnsureConfiguredIndexAsync(db, client, indexUid, ct);
            var ids = await db.Posts.IgnoreQueryFilters().AsNoTracking().Where(post => post.Id > afterPostId)
                .OrderBy(post => post.Id).Select(post => post.Id).Take(ReconciliationBatchSize).ToListAsync(ct);
            if (ids.Count == 0) return;

            var posts = await db.Posts.IgnoreQueryFilters().AsNoTracking().Where(post => ids.Contains(post.Id))
                .Select(post => new
                {
                    post.Id,
                    post.Title,
                    post.Body,
                    post.Excerpt,
                    post.Slug,
                    post.AuthorId,
                    AuthorName = post.Author.Name,
                    post.PublishedAt,
                    post.ArchivedAt,
                    post.IndexVersion
                })
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

            var upsertPosts = posts.Where(post => post.ArchivedAt == null).ToArray();
            var upsertIds = upsertPosts.Select(post => post.Id).ToArray();
            var topicRows = await db.PostTopics.AsNoTracking().Where(topic => upsertIds.Contains(topic.PostId))
                .Select(topic => new { topic.PostId, topic.Topic }).ToListAsync(ct);
            var topics = topicRows.GroupBy(row => row.PostId).ToDictionary(group => group.Key, group => group.Select(row => row.Topic).ToArray());
            if (upsertPosts.Length > 0)
            {
                var documents = upsertPosts.Select(post => new SearchPostDocument(post.Id.ToString(),
                    post.Title, post.Body, post.Excerpt, post.Slug, post.AuthorId, post.AuthorName,
                    topics.GetValueOrDefault(post.Id, []), post.PublishedAt)).ToArray();
                await SubmitBatchAsync(upsertIds, documents);
            }
            var deleteIds = posts.Where(post => post.ArchivedAt != null).Select(post => post.Id).ToArray();
            if (deleteIds.Length > 0) await SubmitBatchAsync(deleteIds, null);

            if (ids.Count == ReconciliationBatchSize)
                await jobs.EnqueueAsync(db, new ReindexSearchBatch(ids[^1]), ct: ct);

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
            await db.CronLeases.Where(lease => lease.Name == LaneName && lease.LastRunAt == laneLeaseAt)
                .ExecuteUpdateAsync(set => set.SetProperty(lease => lease.LockedUntil, (DateTime?)null), CancellationToken.None);
        }
    }
}
