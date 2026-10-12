using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Infrastructure;

namespace SvelteMongoReplicasBlog.Features.Discovery;

/// <summary>Mongo-backed profiles, versioned recommendation snapshots, and live visibility checks.</summary>
public sealed partial class DiscoveryService(
    TimeProvider? timeProvider = null,
    OptionalReadCache? readCache = null
)
{
    readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<DiscoveryPage> GetPageAsync(
        AppDb db,
        Guid userId,
        DiscoveryFeedMode mode,
        string? cursor,
        int pageSize,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        if (userId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(userId));
        if (pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        var modeName = ModeName(mode);
        var now = clock.GetUtcNow().UtcDateTime;
        FeedSnapshot snapshot;
        var offset = 0;
        if (cursor is null or "start")
        {
            var latest = await db
                .FeedSnapshots.Find(x =>
                    x.UserId == userId && x.Mode == modeName && x.ExpiresAt > now
                )
                .SortByDescending(x => x.CreatedAt)
                .Project(x => new SnapshotMetadata(
                    x.Id,
                    x.UserId,
                    x.Mode,
                    x.ProfileSignature,
                    x.CreatedAt,
                    x.ExpiresAt
                ))
                .FirstOrDefaultAsync(ct);
            var signature = await CurrentProfileSignatureAsync(db, userId, mode, now, ct);
            if (
                latest is not null
                && signature.Current
                && latest.ProfileSignature == signature.Value
            )
            {
                snapshot = ToSnapshot(latest);
            }
            else
            {
                var feed = await GetFeedProfileAsync(db, userId, mode, now, ct);
                var items = await BuildFeedItemsAsync(
                    db,
                    userId,
                    mode,
                    feed.Profile,
                    feed.FolloweeIds,
                    now,
                    ct
                );
                signature = await ProfileSignatureAsync(db, mode, feed.FolloweeIds, now, ct);
                snapshot = new FeedSnapshot
                {
                    UserId = userId,
                    Mode = modeName,
                    ProfileSignature = signature.Value,
                    ItemsJson = JsonSerializer.Serialize(items, JsonOptions),
                    CreatedAt = now,
                    ExpiresAt = now.Add(SnapshotLifetime),
                };
                await db.FeedSnapshots.InsertOneAsync(snapshot, cancellationToken: ct);
            }
        }
        else
        {
            var parsed = ParseCursor(cursor);
            var metadata =
                await db
                    .FeedSnapshots.Find(x =>
                        x.Id == parsed.SnapshotId && x.UserId == userId && x.Mode == modeName
                    )
                    .Project(x => new SnapshotMetadata(
                        x.Id,
                        x.UserId,
                        x.Mode,
                        x.ProfileSignature,
                        x.CreatedAt,
                        x.ExpiresAt
                    ))
                    .FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("Feed snapshot does not exist.");
            if (metadata.ExpiresAt <= now)
                throw new KeyNotFoundException("Feed snapshot expired.");
            offset = parsed.Offset;
            snapshot = ToSnapshot(metadata);
        }

        var cacheKey = $"feed-snapshot:{userId}:{modeName}:{snapshot.Id}";
        var all = readCache is null
            ? await LoadSnapshotItemsAsync(db, snapshot.Id, userId, modeName, ct)
            : await readCache.GetOrCreateAsync(
                cacheKey,
                () => LoadSnapshotItemsAsync(db, snapshot.Id, userId, modeName, ct),
                ct,
                SnapshotLifetime
            );
        if (offset < 0 || offset > all.Count)
            throw new ArgumentException("Feed cursor is outside the snapshot.", nameof(cursor));
        var rawPage = all.Skip(offset).Take(pageSize).ToArray();
        var ids = rawPage.Select(item => item.PostId).ToArray();
        var visible =
            ids.Length == 0
                ? []
                : await db
                    .Posts.Find(x => ids.Contains(x.Id) && x.ArchivedAt == null)
                    .Project(x => x.Id)
                    .ToListAsync(ct);
        var seen =
            ids.Length == 0
                ? []
                : await db
                    .SeenPosts.Find(x => x.UserId == userId && ids.Contains(x.PostId))
                    .Project(x => x.PostId)
                    .ToListAsync(ct);
        var visibleAndUnseen = visible.Except(seen).ToHashSet();
        var page = rawPage.Where(item => visibleAndUnseen.Contains(item.PostId)).ToArray();
        var nextOffset = offset + rawPage.Length;
        var hasMore = nextOffset < all.Count;
        return new DiscoveryPage(
            page,
            hasMore ? MakeCursor(snapshot.Id, nextOffset) : null,
            hasMore
        );
    }

    /// <summary>Returns only yesterday's unseen matches against the recipient's own profile.</summary>
    public async Task<IReadOnlyList<DiscoveryItem>> GetDigestCandidatesAsync(
        AppDb db,
        Guid userId,
        DigestWindow window,
        DateTimeOffset nowUtc,
        int limit = 10,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        if (userId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(userId));
        if (limit is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(limit));
        if (window.EndUtc <= window.StartUtc)
            throw new ArgumentException("Digest window must be non-empty.", nameof(window));
        var profile = (await GetProfilesAsync(db, [userId], nowUtc.UtcDateTime, ct))[userId];
        if (!profile.HasSignals)
            return [];
        var candidates = await LoadCandidatesAsync(
            db,
            userId,
            profile,
            window.StartUtc.UtcDateTime,
            window.EndUtc.UtcDateTime,
            MaximumDigestCandidates,
            ct
        );
        return RankAndDescribe(candidates, userId, profile, nowUtc).Take(limit).ToArray();
    }

    /// <summary>Queues dirty or daily-expired profile refreshes in bounded, duplicate-coalesced batches.</summary>
    public async Task<int> RefreshProfilesAsync(
        AppDb db,
        JobQueue jobs,
        CancellationToken ct = default,
        int batchSize = 500
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(jobs);
        if (batchSize is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        var now = clock.GetUtcNow().UtcDateTime;
        var activePayloads = await db
            .Jobs.Find(x =>
                x.Type == nameof(RefreshInterestProfile)
                && (x.Status == JobStatus.Pending || x.Status == JobStatus.Running)
            )
            .Project(x => x.Payload)
            .ToListAsync(ct);
        var active = new HashSet<Guid>();
        foreach (var payload in activePayloads)
        {
            try
            {
                if (
                    JsonSerializer.Deserialize<RefreshInterestProfile>(payload, JsonOptions) is
                    { } job && job.UserId != Guid.Empty
                )
                    active.Add(job.UserId);
            }
            catch (JsonException) { }
        }

        var slots = Math.Max(0, batchSize - active.Count);
        if (slots == 0)
            return 0;
        var due = await db
            .InterestProfiles.Find(x => x.Dirty || x.UpdatedAt < now.Date)
            .SortByDescending(x => x.Dirty)
            .ThenBy(x => x.UpdatedAt)
            .Limit(batchSize + active.Count)
            .Project(x => x.UserId)
            .ToListAsync(ct);
        var queued = 0;
        foreach (var id in due)
        {
            if (active.Contains(id))
                continue;
            await jobs.EnqueueAsync(db, new RefreshInterestProfile(id), ct: ct);
            active.Add(id);
            if (++queued == slots)
                break;
        }

        if (queued > 0)
            jobs.Wake();
        return queued;
    }

    public async Task RefreshProfileAsync(AppDb db, Guid userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (userId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(userId));
        _ = await GetProfilesAsync(db, [userId], clock.GetUtcNow().UtcDateTime, ct);
    }

    /// <summary>Stores an authenticated view and invalidates the viewer's interest profile.</summary>
    public async Task<bool> MarkSeenAsync(
        AppDb db,
        Guid userId,
        Guid postId,
        DateTime atUtc,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        if (userId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(userId));
        if (postId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(postId));
        var at = DateTime.SpecifyKind(atUtc, DateTimeKind.Utc);
        var update = Builders<SeenPost>
            .Update.SetOnInsert(x => x.Id, $"{userId}:{postId}")
            .SetOnInsert(x => x.UserId, userId)
            .SetOnInsert(x => x.PostId, postId)
            .SetOnInsert(x => x.FirstSeenAt, at)
            .Max(x => x.LastSeenAt, at);
        bool recorded;
        try
        {
            recorded = await db.TransactionAsync(
                async session => await PersistSeenAsync(session, update, upsert: true),
                ct
            );
        }
        catch (MongoWriteException error)
            when (error.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // A simultaneous beacon may have inserted this user's row after our snapshot.
            // Retry in a fresh transaction and recheck post visibility before recording it.
            recorded = await db.TransactionAsync(
                async session =>
                    await PersistSeenAsync(
                        session,
                        Builders<SeenPost>.Update.Max(x => x.LastSeenAt, at),
                        upsert: false
                    ),
                ct
            );
        }

        if (recorded)
            await MarkProfileDirtyAsync(db, userId, at, ct);
        return recorded;
        async Task<bool> PersistSeenAsync(
            IClientSessionHandle session,
            UpdateDefinition<SeenPost> seenUpdate,
            bool upsert
        )
        {
            var visible = await db
                .Posts.Find(session, x => x.Id == postId && x.ArchivedAt == null)
                .AnyAsync(ct);
            if (!visible)
                return false;
            var fenced = await db.Posts.UpdateOneAsync(
                session,
                x => x.Id == postId && x.ArchivedAt == null,
                Builders<Post>.Update.Inc(x => x.SocialRevision, 1),
                cancellationToken: ct
            );
            if (fenced.ModifiedCount == 0)
                return false;
            var result = await db.SeenPosts.UpdateOneAsync(
                session,
                x => x.UserId == userId && x.PostId == postId,
                seenUpdate,
                new UpdateOptions { IsUpsert = upsert },
                ct
            );
            if (!upsert && result.MatchedCount == 0)
            {
                await db.SeenPosts.InsertOneAsync(
                    session,
                    new SeenPost
                    {
                        Id = $"{userId}:{postId}",
                        UserId = userId,
                        PostId = postId,
                        FirstSeenAt = at,
                        LastSeenAt = at,
                    },
                    cancellationToken: ct
                );
            }

            return true;
        }
    }

    /// <summary>Increments a monotonic revision so same-clock actions cannot be lost by a concurrent rebuild.</summary>
    public async Task MarkProfileDirtyAsync(
        AppDb db,
        Guid userId,
        DateTime atUtc,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        if (userId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(userId));
        var at = DateTime.SpecifyKind(atUtc, DateTimeKind.Utc);
        var update = Builders<InterestProfile>
            .Update.SetOnInsert(x => x.UserId, userId)
            .SetOnInsert(x => x.TopicsJson, "{}")
            .SetOnInsert(x => x.AuthorsJson, "{}")
            .SetOnInsert(x => x.UpdatedAt, at)
            .Set(x => x.Dirty, true)
            .Inc(x => x.Revision, 1);
        try
        {
            await db.InterestProfiles.UpdateOneAsync(
                x => x.UserId == userId,
                update,
                new UpdateOptions { IsUpsert = true },
                ct
            );
        }
        catch (MongoWriteException error)
            when (error.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            await db.InterestProfiles.UpdateOneAsync(
                x => x.UserId == userId,
                Builders<InterestProfile>.Update.Set(x => x.Dirty, true).Inc(x => x.Revision, 1),
                cancellationToken: ct
            );
        }
    }

    async Task<(string Value, bool Current)> CurrentProfileSignatureAsync(
        AppDb db,
        Guid userId,
        DiscoveryFeedMode mode,
        DateTime now,
        CancellationToken ct
    )
    {
        List<Guid> follows;
        if (mode == DiscoveryFeedMode.FollowingInterests)
            follows = await db
                .Follows.Find(x => x.FollowerId == userId && x.FolloweeId != userId)
                .SortBy(x => x.FolloweeId)
                .Project(x => x.FolloweeId)
                .ToListAsync(ct);
        else if (mode == DiscoveryFeedMode.ForYou)
            follows = [userId];
        else
            throw new ArgumentOutOfRangeException(nameof(mode));
        return await ProfileSignatureAsync(db, mode, follows.ToArray(), now, ct);
    }

    static async Task<(string Value, bool Current)> ProfileSignatureAsync(
        AppDb db,
        DiscoveryFeedMode mode,
        Guid[] followees,
        DateTime now,
        CancellationToken ct
    )
    {
        var ids = followees.Distinct().Order().ToArray();
        var rows =
            ids.Length == 0
                ? []
                : await db
                    .InterestProfiles.Find(x => ids.Contains(x.UserId))
                    .Project(x => new ProfileRevision(x.UserId, x.Revision, x.UpdatedAt, x.Dirty))
                    .ToListAsync(ct);
        var byId = rows.ToDictionary(x => x.UserId);
        var current =
            mode == DiscoveryFeedMode.FollowingInterests && ids.Length == 0
            || ids.Length > 0
                && ids.All(id =>
                    byId.TryGetValue(id, out var row)
                    && !row.Dirty
                    && row.UpdatedAt.Date >= now.Date
                );
        var stableParts = ids.Select(id =>
            byId.TryGetValue(id, out var row)
                ? $"{id}:{row.Revision}:{DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc).Ticks}:{(row.Dirty ? 1 : 0)}"
                : $"{id}:missing"
        );
        var source = $"{ModeName(mode)}|{string.Join('|', stableParts)}";
        return (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))), current);
    }

    async Task<FeedProfile> GetFeedProfileAsync(
        AppDb db,
        Guid userId,
        DiscoveryFeedMode mode,
        DateTime now,
        CancellationToken ct
    )
    {
        if (mode == DiscoveryFeedMode.ForYou)
            return new FeedProfile(
                (await GetProfilesAsync(db, [userId], now, ct))[userId],
                [userId]
            );
        if (mode != DiscoveryFeedMode.FollowingInterests)
            throw new ArgumentOutOfRangeException(nameof(mode));
        var followees = await db
            .Follows.Find(x => x.FollowerId == userId && x.FolloweeId != userId)
            .SortBy(x => x.FolloweeId)
            .Project(x => x.FolloweeId)
            .ToListAsync(ct);
        if (followees.Count == 0)
            return new FeedProfile(EmptyProfile(), []);
        var profiles = await GetProfilesAsync(db, followees, now, ct);
        return new FeedProfile(
            RecommendationScoring.MeanNormalizedProfiles(followees.Select(id => profiles[id])),
            followees.ToArray()
        );
    }

    async Task<Dictionary<Guid, DiscoveryProfile>> GetProfilesAsync(
        AppDb db,
        IReadOnlyCollection<Guid> userIds,
        DateTime now,
        CancellationToken ct
    )
    {
        var ids = userIds.Distinct().Order().ToArray();
        var result = new Dictionary<Guid, DiscoveryProfile>();
        if (ids.Length == 0)
            return result;
        var rows = new Dictionary<Guid, ProfileRevision>();
        foreach (var batch in ids.Chunk(ProfileBatchSize))
        {
            var found = await db
                .InterestProfiles.Find(x => batch.Contains(x.UserId))
                .Project(x => new ProfileRevision(x.UserId, x.Revision, x.UpdatedAt, x.Dirty))
                .ToListAsync(ct);
            foreach (var row in found)
                rows[row.UserId] = row;
        }

        var rebuild = new List<Guid>();
        foreach (var id in ids)
        {
            if (
                !rows.TryGetValue(id, out var revision)
                || revision.Dirty
                || revision.UpdatedAt.Date < now.Date
            )
            {
                rebuild.Add(id);
                continue;
            }

            var key = ProfileCacheKey(id, revision);
            DiscoveryProfile? profile;
            if (readCache is null)
                profile = await LoadFreshProfileAsync(db, id, revision, ct);
            else
                profile = await readCache.GetOrCreateAsync<DiscoveryProfile?>(
                    key,
                    () => LoadFreshProfileAsync(db, id, revision, ct),
                    ct,
                    ProfileCacheLifetime
                );
            if (profile is null)
                rebuild.Add(id);
            else
                result[id] = profile;
        }

        foreach (var batch in rebuild.Chunk(ProfileBatchSize))
        {
            var computed = await BuildProfilesAsync(db, batch, now, ct);
            foreach (var id in batch)
            {
                var profile = computed[id];
                var persisted = false;
                for (var attempt = 0; attempt < 3 && !persisted; attempt++)
                {
                    if (attempt > 0)
                        profile = (await BuildProfilesAsync(db, [id], now, ct))[id];
                    rows.TryGetValue(id, out var expected);
                    if (expected is null)
                    {
                        var insert = new InterestProfile
                        {
                            UserId = id,
                            TopicsJson = JsonSerializer.Serialize(
                                profile.TopicWeights,
                                JsonOptions
                            ),
                            AuthorsJson = JsonSerializer.Serialize(
                                profile.AuthorWeights,
                                JsonOptions
                            ),
                            UpdatedAt = now,
                            Revision = 0,
                            Dirty = false,
                        };
                        try
                        {
                            await db.InterestProfiles.InsertOneAsync(insert, cancellationToken: ct);
                            rows[id] = new ProfileRevision(id, 0, now, false);
                            persisted = true;
                        }
                        catch (MongoWriteException error)
                            when (error.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                        {
                            expected = await ReadRevisionAsync(db, id, ct);
                            if (expected is null)
                                continue;
                            rows[id] = expected;
                        }
                    }
                    else
                    {
                        var filter =
                            Builders<InterestProfile>.Filter.Eq(x => x.UserId, id)
                            & Builders<InterestProfile>.Filter.Eq(
                                x => x.Revision,
                                expected.Revision
                            )
                            & Builders<InterestProfile>.Filter.Eq(
                                x => x.UpdatedAt,
                                expected.UpdatedAt
                            )
                            & Builders<InterestProfile>.Filter.Eq(x => x.Dirty, expected.Dirty);
                        var update = Builders<InterestProfile>
                            .Update.Set(
                                x => x.TopicsJson,
                                JsonSerializer.Serialize(profile.TopicWeights, JsonOptions)
                            )
                            .Set(
                                x => x.AuthorsJson,
                                JsonSerializer.Serialize(profile.AuthorWeights, JsonOptions)
                            )
                            .Set(x => x.UpdatedAt, now)
                            .Set(x => x.Dirty, false);
                        var wrote = await db.InterestProfiles.UpdateOneAsync(
                            filter,
                            update,
                            cancellationToken: ct
                        );
                        if (wrote.ModifiedCount > 0)
                        {
                            rows[id] = new ProfileRevision(id, expected.Revision, now, false);
                            persisted = true;
                        }
                        else
                        {
                            expected = await ReadRevisionAsync(db, id, ct);
                            if (expected is null)
                            {
                                rows.Remove(id);
                                continue;
                            }

                            rows[id] = expected;
                            if (!expected.Dirty && expected.UpdatedAt.Date >= now.Date)
                            {
                                var current = await LoadFreshProfileAsync(db, id, expected, ct);
                                if (current is not null)
                                {
                                    profile = current;
                                    persisted = true;
                                }
                            }
                        }
                    }
                }

                result[id] = profile;
                if (
                    rows.TryGetValue(id, out var stored)
                    && !stored.Dirty
                    && stored.UpdatedAt.Date >= now.Date
                    && readCache is not null
                )
                    await readCache.GetOrCreateAsync(
                        ProfileCacheKey(id, stored),
                        () => Task.FromResult<DiscoveryProfile?>(profile),
                        ct,
                        ProfileCacheLifetime
                    );
            }
        }

        return result;
    }

    async Task<Dictionary<Guid, DiscoveryProfile>> BuildProfilesAsync(
        AppDb db,
        IReadOnlyCollection<Guid> userIds,
        DateTime now,
        CancellationToken ct
    )
    {
        var outputs = userIds.ToDictionary(id => id, _ => new List<DiscoverySignal>());
        if (outputs.Count == 0)
            return [];
        var since = now - RecommendationScoring.ProfileWindow;
        var postIds = new HashSet<Guid>();
        foreach (var batch in userIds.Chunk(ProfileBatchSize))
        {
            var likes = await db
                .Likes.Find(x =>
                    batch.Contains(x.UserId) && x.CreatedAt >= since && x.CreatedAt <= now
                )
                .ToListAsync(ct);
            foreach (var like in likes)
            {
                outputs[like.UserId]
                    .Add(
                        Signal(like.UserId, like.PostId, DiscoverySignalKind.Like, like.CreatedAt)
                    );
                postIds.Add(like.PostId);
            }

            var comments = await db
                .Comments.Find(x =>
                    batch.Contains(x.UserId) && x.CreatedAt >= since && x.CreatedAt <= now
                )
                .ToListAsync(ct);
            foreach (var group in comments.GroupBy(x => (x.UserId, x.PostId)))
            {
                outputs[group.Key.UserId]
                    .Add(
                        Signal(
                            group.Key.UserId,
                            group.Key.PostId,
                            DiscoverySignalKind.Comment,
                            group.Max(x => x.CreatedAt)
                        )
                    );
                postIds.Add(group.Key.PostId);
            }

            var seen = await db
                .SeenPosts.Find(x =>
                    batch.Contains(x.UserId) && x.LastSeenAt >= since && x.LastSeenAt <= now
                )
                .ToListAsync(ct);
            foreach (var view in seen)
            {
                outputs[view.UserId]
                    .Add(
                        Signal(view.UserId, view.PostId, DiscoverySignalKind.View, view.LastSeenAt)
                    );
                postIds.Add(view.PostId);
            }
        }

        var postsById = new Dictionary<Guid, DiscoveryPost>();
        foreach (var batch in postIds.Chunk(PostBatchSize))
        {
            var posts = await db.Posts.Find(x => batch.Contains(x.Id)).ToListAsync(ct);
            var topics = await db.PostTopics.Find(x => batch.Contains(x.PostId)).ToListAsync(ct);
            var byPost = topics
                .GroupBy(x => x.PostId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyCollection<string>)group.Select(x => x.Topic).ToArray()
                );
            foreach (var post in posts)
            {
                postsById[post.Id] = new DiscoveryPost(
                    post.Id.ToString(),
                    post.AuthorId.ToString(),
                    byPost.GetValueOrDefault(post.Id) ?? [],
                    new DateTimeOffset(DateTime.SpecifyKind(post.PublishedAt, DateTimeKind.Utc)),
                    post.ArchivedAt is not null
                );
            }
        }

        var nowUtc = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));
        var postMap = postsById.ToDictionary(
            x => x.Key.ToString(),
            x => x.Value,
            StringComparer.Ordinal
        );
        return outputs.ToDictionary(
            pair => pair.Key,
            pair =>
                RecommendationScoring.BuildProfile(
                    pair.Key.ToString(),
                    pair.Value,
                    postMap,
                    nowUtc
                )
        );
    }

    async Task<DiscoveryProfile?> LoadFreshProfileAsync(
        AppDb db,
        Guid userId,
        ProfileRevision expected,
        CancellationToken ct
    )
    {
        var row = await db
            .InterestProfiles.Find(x =>
                x.UserId == userId
                && x.Revision == expected.Revision
                && x.UpdatedAt == expected.UpdatedAt
                && !x.Dirty
            )
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return null;
        return TryRead(row, out var profile) ? profile : null;
    }

    static async Task<ProfileRevision?> ReadRevisionAsync(
        AppDb db,
        Guid userId,
        CancellationToken ct
    ) =>
        await db
            .InterestProfiles.Find(x => x.UserId == userId)
            .Project(x => new ProfileRevision(x.UserId, x.Revision, x.UpdatedAt, x.Dirty))
            .FirstOrDefaultAsync(ct);

    async Task<IReadOnlyList<DiscoveryItem>> BuildFeedItemsAsync(
        AppDb db,
        Guid userId,
        DiscoveryFeedMode mode,
        DiscoveryProfile profile,
        Guid[] followees,
        DateTime now,
        CancellationToken ct
    )
    {
        if (!profile.HasSignals)
            return mode == DiscoveryFeedMode.FollowingInterests
                ? await BuildColdStartFollowingItemsAsync(db, userId, followees, ct)
                : [];
        var candidates = await LoadCandidatesAsync(
            db,
            userId,
            profile,
            now - RecommendationScoring.ProfileWindow,
            now.AddMilliseconds(1),
            MaximumFeedCandidates,
            ct
        );
        return RankAndDescribe(
                candidates,
                userId,
                profile,
                new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc))
            )
            .Take(MaximumSnapshotItems)
            .ToArray();
    }

    static async Task<IReadOnlyList<DiscoveryItem>> BuildColdStartFollowingItemsAsync(
        AppDb db,
        Guid userId,
        Guid[] followees,
        CancellationToken ct
    )
    {
        if (followees.Length == 0)
            return [];
        var authored = await db
            .Posts.Find(x =>
                followees.Contains(x.AuthorId) && x.AuthorId != userId && x.ArchivedAt == null
            )
            .SortByDescending(x => x.PublishedAt)
            .ThenByDescending(x => x.Id)
            .Limit(MaximumSnapshotItems)
            .ToListAsync(ct);
        var repostedIds = await db
            .Reposts.Find(x => followees.Contains(x.UserId))
            .SortByDescending(x => x.CreatedAt)
            .Limit(MaximumSnapshotItems)
            .Project(x => x.PostId)
            .ToListAsync(ct);
        var reposted =
            repostedIds.Count == 0
                ? []
                : await db
                    .Posts.Find(x =>
                        repostedIds.Contains(x.Id) && x.AuthorId != userId && x.ArchivedAt == null
                    )
                    .SortByDescending(x => x.PublishedAt)
                    .ThenByDescending(x => x.Id)
                    .Limit(MaximumSnapshotItems)
                    .ToListAsync(ct);
        var candidates = authored
            .Concat(reposted)
            .GroupBy(post => post.Id)
            .Select(group => group.First())
            .OrderByDescending(post => post.PublishedAt)
            .ThenByDescending(post => post.Id, GuidByteOrder.Instance)
            .ToArray();
        if (candidates.Length == 0)
            return [];
        var ids = candidates.Select(post => post.Id).ToArray();
        var seen = await db
            .SeenPosts.Find(x => x.UserId == userId && ids.Contains(x.PostId))
            .Project(x => x.PostId)
            .ToListAsync(ct);
        var seenSet = seen.ToHashSet();
        return candidates
            .Where(post => !seenSet.Contains(post.Id))
            .Take(MaximumSnapshotItems)
            .Select(post => new DiscoveryItem(post.Id, 0.05, "From someone you follow"))
            .ToArray();
    }

    async Task<List<DiscoveryPost>> LoadCandidatesAsync(
        AppDb db,
        Guid userId,
        DiscoveryProfile profile,
        DateTime fromUtc,
        DateTime? toUtcExclusive,
        int maximum,
        CancellationToken ct
    )
    {
        var topicIds = profile.TopicWeights.Keys.ToArray();
        var authorIds = profile
            .AuthorWeights.Keys.Select(key =>
                Guid.TryParse(key, out var id)
                    ? (Guid?)id
                    : null
            )
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToArray();
        if (topicIds.Length == 0 && authorIds.Length == 0)
            return [];
        var baseFilter = Builders<Post>.Filter.And(
            Builders<Post>.Filter.Ne(x => x.AuthorId, userId),
            Builders<Post>.Filter.Eq(x => x.ArchivedAt, null),
            Builders<Post>.Filter.Gte(x => x.PublishedAt, fromUtc)
        );
        if (toUtcExclusive is { } end)
            baseFilter &= Builders<Post>.Filter.Lt(x => x.PublishedAt, end);
        var authorRows =
            authorIds.Length == 0
                ? []
                : await db
                    .Posts.Find(baseFilter & Builders<Post>.Filter.In(x => x.AuthorId, authorIds))
                    .SortByDescending(x => x.PublishedAt)
                    .ThenByDescending(x => x.Id)
                    .Limit(maximum)
                    .ToListAsync(ct);
        var topicPostIds =
            topicIds.Length == 0
                ? []
                : await db
                    .PostTopics.Find(x => topicIds.Contains(x.Topic))
                    .SortByDescending(x => x.PostId)
                    .Limit(maximum)
                    .Project(x => x.PostId)
                    .ToListAsync(ct);
        var topicRows =
            topicPostIds.Count == 0
                ? []
                : await db
                    .Posts.Find(
                        baseFilter & Builders<Post>.Filter.In(x => x.Id, topicPostIds.Distinct())
                    )
                    .SortByDescending(x => x.PublishedAt)
                    .ThenByDescending(x => x.Id)
                    .Limit(maximum)
                    .ToListAsync(ct);
        var candidates = authorRows
            .Concat(topicRows)
            .GroupBy(post => post.Id)
            .Select(group => group.First())
            .OrderByDescending(post => post.PublishedAt)
            .ThenByDescending(post => post.Id, GuidByteOrder.Instance)
            .ToArray();
        if (candidates.Length == 0)
            return [];
        var ids = candidates.Select(post => post.Id).ToArray();
        var seen = await db
            .SeenPosts.Find(x => x.UserId == userId && ids.Contains(x.PostId))
            .Project(x => x.PostId)
            .ToListAsync(ct);
        var seenSet = seen.ToHashSet();
        var eligible = candidates.Where(post => !seenSet.Contains(post.Id)).Take(maximum).ToArray();
        if (eligible.Length == 0)
            return [];
        var eligibleIds = eligible.Select(post => post.Id).ToArray();
        var topics = await db.PostTopics.Find(x => eligibleIds.Contains(x.PostId)).ToListAsync(ct);
        var byPost = topics
            .GroupBy(x => x.PostId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<string>)group.Select(x => x.Topic).ToArray()
            );
        return eligible
            .Select(post => new DiscoveryPost(
                post.Id.ToString(),
                post.AuthorId.ToString(),
                byPost.GetValueOrDefault(post.Id) ?? [],
                new DateTimeOffset(DateTime.SpecifyKind(post.PublishedAt, DateTimeKind.Utc)),
                post.ArchivedAt is not null
            ))
            .ToList();
    }

    static async Task<IReadOnlyList<DiscoveryItem>> LoadSnapshotItemsAsync(
        AppDb db,
        string id,
        Guid userId,
        string mode,
        CancellationToken ct
    )
    {
        var json =
            await db
                .FeedSnapshots.Find(x => x.Id == id && x.UserId == userId && x.Mode == mode)
                .Project(x => x.ItemsJson)
                .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Feed snapshot does not exist.");
        return JsonSerializer.Deserialize<List<DiscoveryItem>>(json, JsonOptions)
            ?? throw new InvalidDataException("Feed snapshot has no item list.");
    }
}
