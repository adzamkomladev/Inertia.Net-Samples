using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Features.Discovery;

/// <summary>Loads cached profiles, builds stable ranked snapshots, and tracks authenticated post views.</summary>
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
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        if (userId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(userId));
        if (pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        var now = clock.GetUtcNow().UtcDateTime;
        FeedSnapshot snapshot;
        var snapshotHasJson = false;
        var offset = 0;
        if (cursor is null)
        {
            var modeName = ModeName(mode);
            var latest = await db
                .FeedSnapshots.AsNoTracking()
                .Where(row => row.UserId == userId && row.Mode == modeName && row.ExpiresAt > now)
                .OrderByDescending(row => row.CreatedAt)
                .Select(row => new SnapshotMetadata(
                    row.Id,
                    row.UserId,
                    row.Mode,
                    row.CreatedAt,
                    row.ExpiresAt
                ))
                .FirstOrDefaultAsync(cancellationToken);
            if (
                latest is not null
                && await IsSnapshotCurrentAsync(db, userId, mode, latest, now, cancellationToken)
            )
            {
                snapshot = ToSnapshot(latest);
            }
            else
            {
                var profile = await GetFeedProfileAsync(db, userId, mode, now, cancellationToken);
                var items = await BuildFeedItemsAsync(
                    db,
                    userId,
                    mode,
                    profile,
                    now,
                    cancellationToken
                );
                snapshot = new FeedSnapshot
                {
                    UserId = userId,
                    Mode = modeName,
                    ItemsJson = JsonSerializer.Serialize(items, JsonOptions),
                    CreatedAt = now,
                    ExpiresAt = now + SnapshotLifetime,
                };
                db.FeedSnapshots.Add(snapshot);
                await db.SaveChangesAsync(cancellationToken);
                snapshotHasJson = true;
            }
        }
        else
        {
            var parsed = ParseCursor(cursor);
            var metadata =
                await db
                    .FeedSnapshots.AsNoTracking()
                    .Where(s =>
                        s.Id == parsed.SnapshotId && s.UserId == userId && s.Mode == ModeName(mode)
                    )
                    .Select(s => new SnapshotMetadata(
                        s.Id,
                        s.UserId,
                        s.Mode,
                        s.CreatedAt,
                        s.ExpiresAt
                    ))
                    .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("Feed snapshot does not exist.");
            snapshot = ToSnapshot(metadata);
            if (DateTime.SpecifyKind(snapshot.ExpiresAt, DateTimeKind.Utc) <= now)
                throw new KeyNotFoundException("Feed snapshot has expired.");
            offset = parsed.Offset;
        }

        var cacheKey = $"feed-snapshot:{userId}:{ModeName(mode)}:{snapshot.Id}";
        IReadOnlyList<DiscoveryItem> allItems;
        if (readCache is null)
        {
            var json = snapshotHasJson
                ? snapshot.ItemsJson
                : await LoadSnapshotJsonAsync(db, snapshot.Id, userId, mode, cancellationToken);
            allItems = DeserializeItems(json);
        }
        else
        {
            allItems = await readCache.GetOrCreateAsync(
                cacheKey,
                async () =>
                {
                    var json = snapshotHasJson
                        ? snapshot.ItemsJson
                        : await LoadSnapshotJsonAsync(
                            db,
                            snapshot.Id,
                            userId,
                            mode,
                            cancellationToken
                        );
                    return DeserializeItems(json);
                },
                cancellationToken,
                SnapshotLifetime
            );
        }

        if (offset < 0 || offset > allItems.Count)
            throw new ArgumentException("Feed cursor is outside the snapshot.", nameof(cursor));
        var rawPage = allItems.Skip(offset).Take(pageSize).ToArray();
        var ids = rawPage.Select(item => item.PostId).ToArray();
        var visibleIds =
            ids.Length == 0
                ? new HashSet<Guid>()
                : (
                    await db
                        .Posts.AsNoTracking()
                        .Where(post =>
                            ids.Contains(post.Id)
                            && post.ArchivedAt == null
                            && !db.SeenPosts.Any(seen =>
                                seen.UserId == userId && seen.PostId == post.Id
                            )
                        )
                        .Select(post => post.Id)
                        .ToListAsync(cancellationToken)
                ).ToHashSet();
        var pageItems = rawPage.Where(item => visibleIds.Contains(item.PostId)).ToArray();
        var nextOffset = offset + rawPage.Length;
        var hasMore = nextOffset < allItems.Count;
        var nextCursor = hasMore ? MakeCursor(snapshot.Id, nextOffset) : null;
        return new DiscoveryPage(pageItems, nextCursor, hasMore);
    }

    async Task<bool> IsSnapshotCurrentAsync(
        AppDb db,
        Guid userId,
        DiscoveryFeedMode mode,
        SnapshotMetadata snapshot,
        DateTime nowUtc,
        CancellationToken cancellationToken
    )
    {
        var createdAt = DateTime.SpecifyKind(snapshot.CreatedAt, DateTimeKind.Utc);
        if (mode == DiscoveryFeedMode.ForYou)
        {
            var profile = await db
                .InterestProfiles.AsNoTracking()
                .Where(row => row.UserId == userId)
                .Select(row => new ProfileVersion(row.UserId, row.UpdatedAt, row.Dirty))
                .FirstOrDefaultAsync(cancellationToken);
            return profile is not null
                && !profile.Dirty
                && DateTime.SpecifyKind(profile.UpdatedAt, DateTimeKind.Utc) <= createdAt
                && DateTime.SpecifyKind(profile.UpdatedAt, DateTimeKind.Utc).Date >= nowUtc.Date;
        }

        if (mode != DiscoveryFeedMode.FollowingInterests)
            throw new ArgumentOutOfRangeException(nameof(mode));
        var followedIds = await db
            .Follows.AsNoTracking()
            .Where(row => row.FollowerId == userId)
            .Select(row => row.FolloweeId)
            .ToArrayAsync(cancellationToken);
        if (followedIds.Length == 0)
            return true;
        if (
            await db
                .Follows.AsNoTracking()
                .AnyAsync(
                    row => row.FollowerId == userId && row.CreatedAt > createdAt,
                    cancellationToken
                )
        )
            return false;
        var versions = await db
            .InterestProfiles.AsNoTracking()
            .Where(row => followedIds.Contains(row.UserId))
            .Select(row => new ProfileVersion(row.UserId, row.UpdatedAt, row.Dirty))
            .ToListAsync(cancellationToken);
        return versions.Count == followedIds.Length
            && versions.All(profile =>
                !profile.Dirty
                && DateTime.SpecifyKind(profile.UpdatedAt, DateTimeKind.Utc) <= createdAt
                && DateTime.SpecifyKind(profile.UpdatedAt, DateTimeKind.Utc).Date >= nowUtc.Date
            );
    }

    static async Task<string> LoadSnapshotJsonAsync(
        AppDb db,
        string snapshotId,
        Guid userId,
        DiscoveryFeedMode mode,
        CancellationToken cancellationToken
    ) =>
        await db
            .FeedSnapshots.AsNoTracking()
            .Where(row =>
                row.Id == snapshotId && row.UserId == userId && row.Mode == ModeName(mode)
            )
            .Select(row => row.ItemsJson)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new KeyNotFoundException("Feed snapshot does not exist.");

    /// <summary>Ranks yesterday's unseen posts against the recipient's personal profile only.</summary>
    public async Task<IReadOnlyList<DiscoveryItem>> GetDigestCandidatesAsync(
        AppDb db,
        Guid userId,
        DigestWindow window,
        DateTimeOffset nowUtc,
        int limit = 5,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        if (userId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(userId));
        if (limit is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(limit));
        if (window.EndUtc <= window.StartUtc)
            throw new ArgumentException("Digest window must be non-empty.", nameof(window));
        var profile = (await GetProfilesAsync(db, [userId], nowUtc.UtcDateTime, cancellationToken))[
            userId
        ];
        if (!profile.HasSignals)
            return [];
        var candidates = await LoadCandidatesAsync(
            db,
            userId,
            profile,
            window.StartUtc.UtcDateTime,
            window.EndUtc.UtcDateTime,
            MaximumDigestCandidates,
            cancellationToken
        );
        return RankAndDescribe(candidates, userId, profile, nowUtc).Take(limit).ToArray();
    }

    /// <summary>Queues dirty or daily-expired profiles in bounded, duplicate-coalesced batches.</summary>
    public async Task<int> RefreshProfilesAsync(
        AppDb db,
        JobQueue jobs,
        CancellationToken cancellationToken = default,
        int batchSize = 500
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(jobs);
        if (batchSize is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        var now = clock.GetUtcNow().UtcDateTime;
        var dueIds = await db
            .InterestProfiles.AsNoTracking()
            .Where(profile => profile.Dirty || profile.UpdatedAt < now.Date)
            .OrderByDescending(profile => profile.Dirty)
            .ThenBy(profile => profile.UpdatedAt)
            .Select(profile => profile.UserId)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        if (dueIds.Count == 0)
            return 0;
        var activePayloads = await db
            .Jobs.AsNoTracking()
            .Where(job =>
                job.Type == nameof(RefreshInterestProfile)
                && (job.Status == JobStatus.Pending || job.Status == JobStatus.Running)
            )
            .Select(job => job.Payload)
            .ToListAsync(cancellationToken);
        var activeIds = new HashSet<Guid>();
        foreach (var payload in activePayloads)
        {
            try
            {
                if (
                    JsonSerializer.Deserialize<RefreshInterestProfile>(payload) is
                    { } job && job.UserId != Guid.Empty
                )
                    activeIds.Add(job.UserId);
            }
            catch (JsonException) { }
        }

        var queued = 0;
        foreach (var id in dueIds)
        {
            if (activeIds.Contains(id))
                continue;
            await jobs.EnqueueAsync(db, new RefreshInterestProfile(id), ct: cancellationToken);
            queued++;
        }

        return queued;
    }

    /// <summary>Runs one coalesced profile refresh. Concurrent new signals keep the row dirty.</summary>
    public async Task RefreshProfileAsync(
        AppDb db,
        Guid userId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        if (userId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(userId));
        _ = await GetProfilesAsync(db, [userId], clock.GetUtcNow().UtcDateTime, cancellationToken);
    }

    /// <summary>Persists a first/last authenticated view and invalidates that user's cached profile.</summary>
    public async Task<bool> MarkSeenAsync(
        AppDb db,
        Guid userId,
        Guid postId,
        DateTime atUtc,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        if (userId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(userId));
        if (postId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(postId));
        var at = DateTime.SpecifyKind(atUtc, DateTimeKind.Utc);
        if (!await db.Posts.AnyAsync(post => post.Id == postId, cancellationToken))
            return false;
        // Persist caller changes inside its ambient transaction before the atomic upserts. This
        // makes duplicate view beacons safe and avoids a concurrent composite-key insert race.
        await db.SaveChangesAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $$"""
            INSERT INTO "SeenPosts" ("UserId", "PostId", "FirstSeenAt", "LastSeenAt")
            VALUES ({{userId}}, {{postId}}, {{at}}, {{at}})
            ON CONFLICT("UserId", "PostId") DO UPDATE SET
                "LastSeenAt" = CASE WHEN excluded."LastSeenAt" > "SeenPosts"."LastSeenAt"
                    THEN excluded."LastSeenAt" ELSE "SeenPosts"."LastSeenAt" END
            """,
            cancellationToken
        );
        await UpsertDirtyProfileAsync(db, userId, at, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task MarkProfileDirtyAsync(
        AppDb db,
        Guid userId,
        DateTime atUtc,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        if (userId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(userId));
        var at = DateTime.SpecifyKind(atUtc, DateTimeKind.Utc);
        await db.SaveChangesAsync(cancellationToken);
        await UpsertDirtyProfileAsync(db, userId, at, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    static Task<int> UpsertDirtyProfileAsync(
        AppDb db,
        Guid userId,
        DateTime atUtc,
        CancellationToken cancellationToken
    ) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $$"""
            INSERT INTO "InterestProfiles" ("UserId", "TopicsJson", "AuthorsJson", "UpdatedAt", "Dirty")
            VALUES ({{userId}}, '{}', '{}', {{atUtc}}, TRUE)
            ON CONFLICT("UserId") DO UPDATE SET
                "Dirty" = TRUE,
                "UpdatedAt" = CASE
                    WHEN excluded."UpdatedAt" > "InterestProfiles"."UpdatedAt" THEN excluded."UpdatedAt"
                    ELSE "InterestProfiles"."UpdatedAt" + INTERVAL '1 millisecond'
                END
            """,
            cancellationToken
        );

    async Task<DiscoveryProfile> GetFeedProfileAsync(
        AppDb db,
        Guid userId,
        DiscoveryFeedMode mode,
        DateTime nowUtc,
        CancellationToken cancellationToken
    )
    {
        if (mode == DiscoveryFeedMode.ForYou)
            return (await GetProfilesAsync(db, [userId], nowUtc, cancellationToken))[userId];
        if (mode != DiscoveryFeedMode.FollowingInterests)
            throw new ArgumentOutOfRangeException(nameof(mode));
        var followedIds = await db
            .Follows.AsNoTracking()
            .Where(follow => follow.FollowerId == userId)
            .OrderBy(follow => follow.FolloweeId)
            .Select(follow => follow.FolloweeId)
            .ToListAsync(cancellationToken);
        if (followedIds.Count == 0)
            return EmptyProfile();
        var profiles = await GetProfilesAsync(db, followedIds, nowUtc, cancellationToken);
        return RecommendationScoring.MeanNormalizedProfiles(followedIds.Select(id => profiles[id]));
    }

    async Task<Dictionary<Guid, DiscoveryProfile>> GetProfilesAsync(
        AppDb db,
        IReadOnlyCollection<Guid> userIds,
        DateTime nowUtc,
        CancellationToken cancellationToken
    )
    {
        var ids = userIds.Distinct().ToArray();
        var result = new Dictionary<Guid, DiscoveryProfile>();
        if (ids.Length == 0)
            return result;
        var versions = new Dictionary<Guid, ProfileVersion>();
        foreach (var batch in ids.Chunk(ProfileBatchSize))
        {
            var rows = await db
                .InterestProfiles.AsNoTracking()
                .Where(profile => batch.Contains(profile.UserId))
                .Select(profile => new ProfileVersion(
                    profile.UserId,
                    profile.UpdatedAt,
                    profile.Dirty
                ))
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
                versions[row.UserId] = row;
        }

        var rebuild = new List<Guid>();
        var loadJson = new List<Guid>();
        foreach (var id in ids)
        {
            if (
                !versions.TryGetValue(id, out var version)
                || version.Dirty
                || DateTime.SpecifyKind(version.UpdatedAt, DateTimeKind.Utc).Date < nowUtc.Date
            )
                rebuild.Add(id);
            else if (readCache is not null)
            {
                var key = ProfileCacheKey(id, version.UpdatedAt);
                var cached = await readCache.TryGetAsync<DiscoveryProfile>(key, cancellationToken);
                if (cached is null)
                    loadJson.Add(id);
                else
                    result[id] = cached;
            }
            else
                loadJson.Add(id);
        }

        // Validate the dirty flag/version in SQL first. Read the larger JSON payload only for
        // profiles missing from the versioned Redis cache, in bounded batches.
        var missBatches = new List<Guid[]>(loadJson.Chunk(ProfileBatchSize));
        foreach (var batch in missBatches)
        {
            var rows = await db
                .InterestProfiles.AsNoTracking()
                .Where(profile => batch.Contains(profile.UserId) && !profile.Dirty)
                .Select(profile => new
                {
                    profile.UserId,
                    profile.UpdatedAt,
                    profile.TopicsJson,
                    profile.AuthorsJson,
                })
                .ToListAsync(cancellationToken);
            var byId = rows.ToDictionary(row => row.UserId);
            foreach (var id in batch)
            {
                if (
                    !versions.TryGetValue(id, out var version)
                    || !byId.TryGetValue(id, out var row)
                    || row.UpdatedAt != version.UpdatedAt
                    || !TryDeserializeProfile(row.TopicsJson, row.AuthorsJson, out var profile)
                )
                {
                    rebuild.Add(id);
                    continue;
                }

                result[id] = profile;
                if (readCache is not null)
                    await readCache.SetAsync(
                        ProfileCacheKey(id, version.UpdatedAt),
                        profile,
                        cancellationToken,
                        ProfileCacheLifetime
                    );
            }
        }

        foreach (var batch in rebuild.Chunk(ProfileBatchSize))
        {
            var computed = await BuildProfilesAsync(db, batch, nowUtc, cancellationToken);
            foreach (var id in batch)
            {
                var profile = computed[id];
                var topicsJson = JsonSerializer.Serialize(profile.TopicWeights, JsonOptions);
                var authorsJson = JsonSerializer.Serialize(profile.AuthorWeights, JsonOptions);
                if (versions.TryGetValue(id, out var existing))
                {
                    // Compare the dirty row's timestamp so a concurrent interaction cannot be
                    // overwritten by a profile rebuild that began before that interaction.
                    var wrote = await db
                        .InterestProfiles.Where(row =>
                            row.UserId == id && row.UpdatedAt == existing.UpdatedAt
                        )
                        .ExecuteUpdateAsync(
                            setters =>
                                setters
                                    .SetProperty(row => row.TopicsJson, topicsJson)
                                    .SetProperty(row => row.AuthorsJson, authorsJson)
                                    .SetProperty(row => row.UpdatedAt, nowUtc)
                                    .SetProperty(row => row.Dirty, false),
                            cancellationToken
                        );
                    if (wrote == 0)
                    {
                        var latest = await db
                            .InterestProfiles.AsNoTracking()
                            .FirstOrDefaultAsync(row => row.UserId == id, cancellationToken);
                        result[id] =
                            latest is not null && TryDeserializeProfile(latest, out var current)
                                ? current
                                : profile;
                        continue;
                    }

                    if (readCache is not null)
                        await readCache.SetAsync(
                            ProfileCacheKey(id, nowUtc),
                            profile,
                            cancellationToken,
                            ProfileCacheLifetime
                        );
                }
                else
                {
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $$"""
                        INSERT INTO "InterestProfiles" ("UserId", "TopicsJson", "AuthorsJson", "UpdatedAt", "Dirty")
                        VALUES ({{id}}, {{topicsJson}}, {{authorsJson}}, {{nowUtc}}, FALSE)
                        ON CONFLICT("UserId") DO NOTHING
                        """,
                        cancellationToken
                    );
                }

                result[id] = profile;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    async Task<Dictionary<Guid, DiscoveryProfile>> BuildProfilesAsync(
        AppDb db,
        IReadOnlyCollection<Guid> userIds,
        DateTime nowUtc,
        CancellationToken cancellationToken
    )
    {
        var output = userIds.ToDictionary(id => id, _ => new List<DiscoverySignal>());
        var since = nowUtc - RecommendationScoring.ProfileWindow;
        var postIds = new HashSet<Guid>();
        var likes = await db
            .Likes.AsNoTracking()
            .Where(like =>
                userIds.Contains(like.UserId) && like.CreatedAt >= since && like.CreatedAt <= nowUtc
            )
            .Select(like => new
            {
                like.UserId,
                like.PostId,
                like.CreatedAt,
            })
            .ToListAsync(cancellationToken);
        foreach (var like in likes)
        {
            output[like.UserId]
                .Add(Signal(like.UserId, like.PostId, DiscoverySignalKind.Like, like.CreatedAt));
            postIds.Add(like.PostId);
        }

        var comments = await db
            .Comments.AsNoTracking()
            .Where(comment =>
                userIds.Contains(comment.UserId)
                && comment.CreatedAt >= since
                && comment.CreatedAt <= nowUtc
            )
            .GroupBy(comment => new { comment.UserId, comment.PostId })
            .Select(group => new
            {
                group.Key.UserId,
                group.Key.PostId,
                At = group.Max(comment => comment.CreatedAt),
            })
            .ToListAsync(cancellationToken);
        foreach (var comment in comments)
        {
            output[comment.UserId]
                .Add(
                    Signal(comment.UserId, comment.PostId, DiscoverySignalKind.Comment, comment.At)
                );
            postIds.Add(comment.PostId);
        }

        var views = await db
            .SeenPosts.AsNoTracking()
            .Where(seen =>
                userIds.Contains(seen.UserId)
                && seen.LastSeenAt >= since
                && seen.LastSeenAt <= nowUtc
            )
            .Select(seen => new
            {
                seen.UserId,
                seen.PostId,
                seen.LastSeenAt,
            })
            .ToListAsync(cancellationToken);
        foreach (var view in views)
        {
            output[view.UserId]
                .Add(Signal(view.UserId, view.PostId, DiscoverySignalKind.View, view.LastSeenAt));
            postIds.Add(view.PostId);
        }

        var postRows = new Dictionary<Guid, (Guid AuthorId, DateTime PublishedAt)>();
        var topicRows = new Dictionary<Guid, List<string>>();
        foreach (var batch in postIds.Chunk(PostBatchSize))
        {
            var rows = await db
                .Posts.AsNoTracking()
                .Where(post => batch.Contains(post.Id))
                .Select(post => new
                {
                    post.Id,
                    post.AuthorId,
                    post.PublishedAt,
                })
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
                postRows[row.Id] = (row.AuthorId, row.PublishedAt);
            var topics = await db
                .PostTopics.AsNoTracking()
                .Where(topic => batch.Contains(topic.PostId))
                .Select(topic => new { topic.PostId, topic.Topic })
                .ToListAsync(cancellationToken);
            foreach (var topic in topics)
            {
                if (!topicRows.TryGetValue(topic.PostId, out var values))
                    topicRows[topic.PostId] = values = [];
                values.Add(topic.Topic);
            }
        }

        var postMap = postRows.ToDictionary(
            pair => pair.Key.ToString(),
            pair =>
            {
                var published = DateTime.SpecifyKind(pair.Value.PublishedAt, DateTimeKind.Utc);
                return new DiscoveryPost(
                    pair.Key.ToString(),
                    pair.Value.AuthorId.ToString(),
                    topicRows.GetValueOrDefault(pair.Key) ?? [],
                    new DateTimeOffset(published)
                );
            },
            StringComparer.Ordinal
        );
        var now = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc));
        return output.ToDictionary(
            pair => pair.Key,
            pair =>
                RecommendationScoring.BuildProfile(
                    pair.Key.ToString(),
                    pair.Value,
                    postMap,
                    now
                )
        );
    }

    async Task<IReadOnlyList<DiscoveryItem>> BuildFeedItemsAsync(
        AppDb db,
        Guid userId,
        DiscoveryFeedMode mode,
        DiscoveryProfile profile,
        DateTime nowUtc,
        CancellationToken cancellationToken
    )
    {
        if (!profile.HasSignals)
            return mode == DiscoveryFeedMode.FollowingInterests
                ? await BuildColdStartFollowingItemsAsync(db, userId, cancellationToken)
                : [];
        var candidates = await LoadCandidatesAsync(
            db,
            userId,
            profile,
            nowUtc - RecommendationScoring.ProfileWindow,
            null,
            MaximumFeedCandidates,
            cancellationToken
        );
        return RankAndDescribe(
                candidates,
                userId,
                profile,
                new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc))
            )
            .Take(MaximumSnapshotItems)
            .ToArray();
    }

    static async Task<IReadOnlyList<DiscoveryItem>> BuildColdStartFollowingItemsAsync(
        AppDb db,
        Guid userId,
        CancellationToken cancellationToken
    )
    {
        var followedIds = await db
            .Follows.AsNoTracking()
            .Where(follow => follow.FollowerId == userId)
            .Select(follow => follow.FolloweeId)
            .ToArrayAsync(cancellationToken);
        if (followedIds.Length == 0)
            return [];
        var repostedPostIds = db
            .Reposts.AsNoTracking()
            .Where(repost => followedIds.Contains(repost.UserId))
            .Select(repost => repost.PostId);
        var rows = await db
            .Posts.AsNoTracking()
            .Where(post =>
                post.AuthorId != userId
                && (followedIds.Contains(post.AuthorId) || repostedPostIds.Contains(post.Id))
                && !db.SeenPosts.Any(seen => seen.UserId == userId && seen.PostId == post.Id)
            )
            .OrderByDescending(post => post.PublishedAt)
            .ThenByDescending(post => post.Id)
            .Take(MaximumSnapshotItems)
            .Select(post => new { post.Id, post.PublishedAt })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new DiscoveryItem(row.Id, 0.05, "From someone you follow"))
            .ToArray();
    }

    async Task<List<DiscoveryPost>> LoadCandidatesAsync(
        AppDb db,
        Guid userId,
        DiscoveryProfile profile,
        DateTime fromUtc,
        DateTime? toUtcExclusive,
        int maximum,
        CancellationToken cancellationToken
    )
    {
        var topicIds = profile.TopicWeights.Keys.ToArray();
        var authorIds = profile
            .AuthorWeights.Keys.Select(id =>
                Guid.TryParse(id, out var value)
                    ? (Guid?)value
                    : null
            )
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToArray();
        if (topicIds.Length == 0 && authorIds.Length == 0)
            return [];
        var query = db
            .Posts.AsNoTracking()
            .Where(post =>
                post.AuthorId != userId
                && post.PublishedAt >= fromUtc
                && (toUtcExclusive == null || post.PublishedAt < toUtcExclusive.Value)
                && !db.SeenPosts.Any(seen => seen.UserId == userId && seen.PostId == post.Id)
            )
            .Where(post =>
                (authorIds.Length > 0 && authorIds.Contains(post.AuthorId))
                || (
                    topicIds.Length > 0
                    && db.PostTopics.Any(topic =>
                        topic.PostId == post.Id && topicIds.Contains(topic.Topic)
                    )
                )
            );
        var rows = await query
            .OrderByDescending(post => post.PublishedAt)
            .ThenByDescending(post => post.Id)
            .Take(maximum)
            .Select(post => new
            {
                post.Id,
                post.AuthorId,
                post.PublishedAt,
            })
            .ToListAsync(cancellationToken);
        var ids = rows.Select(row => row.Id).ToArray();
        var topicRows =
            ids.Length == 0
                ? []
                : await db
                    .PostTopics.AsNoTracking()
                    .Where(topic => ids.Contains(topic.PostId))
                    .Select(topic => new { topic.PostId, topic.Topic })
                    .ToListAsync(cancellationToken);
        var topicsByPost = topicRows
            .GroupBy(topic => topic.PostId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<string>)group.Select(topic => topic.Topic).ToArray()
            );
        return rows.Select(row => new DiscoveryPost(
                row.Id.ToString(),
                row.AuthorId.ToString(),
                topicsByPost.GetValueOrDefault(row.Id) ?? [],
                new DateTimeOffset(DateTime.SpecifyKind(row.PublishedAt, DateTimeKind.Utc))
            ))
            .ToList();
    }
}
