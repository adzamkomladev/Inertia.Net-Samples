using System.Text.Json;
using ReactSqliteBlog.Data;
using ReactSqliteBlog.Infrastructure;
using ReactSqliteBlog.Jobs;
using Microsoft.EntityFrameworkCore;

namespace ReactSqliteBlog.Features.Discovery;

/// <summary>Builds stable ranked snapshots and keeps sparse interest profiles current.</summary>
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
        if (userId == Guid.Empty)
            throw new ArgumentException("A user id is required.", nameof(userId));
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
                snapshot = ToSnapshot(latest);
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
                $"feed-snapshot:{userId}:{ModeName(mode)}:{snapshot.Id}",
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
                cancellationToken
            );
        }

        if (offset < 0 || offset > allItems.Count)
            throw new ArgumentException("Feed cursor is outside the snapshot.", nameof(cursor));
        var rawPage = allItems.Skip(offset).Take(pageSize).ToArray();
        var ids = rawPage.Select(item => item.PostId).ToArray();
        var visible =
            ids.Length == 0
                ? new HashSet<Guid>()
                : (
                    await db
                        .Posts.AsNoTracking()
                        .Where(post =>
                            ids.Contains(post.Id)
                            && !db.SeenPosts.Any(seen =>
                                seen.UserId == userId && seen.PostId == post.Id
                            )
                        )
                        .Select(post => post.Id)
                        .ToListAsync(cancellationToken)
                ).ToHashSet();
        var pageItems = rawPage.Where(item => visible.Contains(item.PostId)).ToArray();
        var nextOffset = offset + rawPage.Length;
        var hasMore = nextOffset < allItems.Count;
        return new DiscoveryPage(
            pageItems,
            hasMore ? MakeCursor(snapshot.Id, nextOffset) : null,
            hasMore
        );
    }

    async Task<bool> IsSnapshotCurrentAsync(
        AppDb db,
        Guid userId,
        DiscoveryFeedMode mode,
        SnapshotMetadata snapshot,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var createdAt = DateTime.SpecifyKind(snapshot.CreatedAt, DateTimeKind.Utc);
        if (mode == DiscoveryFeedMode.ForYou)
        {
            var profile = await db
                .InterestProfiles.AsNoTracking()
                .Where(row => row.UserId == userId)
                .Select(row => new ProfileVersion(row.UserId, row.UpdatedAt, row.Dirty))
                .FirstOrDefaultAsync(ct);
            return profile is not null
                && !profile.Dirty
                && DateTime.SpecifyKind(profile.UpdatedAt, DateTimeKind.Utc) <= createdAt
                && DateTime.SpecifyKind(profile.UpdatedAt, DateTimeKind.Utc).Date >= nowUtc.Date;
        }

        if (mode != DiscoveryFeedMode.FollowingInterests)
            throw new ArgumentOutOfRangeException(nameof(mode));
        var follows = await db
            .Follows.AsNoTracking()
            .Where(row => row.FollowerId == userId)
            .OrderBy(row => row.FolloweeId)
            .Select(row => row.FolloweeId)
            .Take(500)
            .ToArrayAsync(ct);
        if (follows.Length == 0)
            return true;
        if (
            await db
                .Follows.AsNoTracking()
                .AnyAsync(row => row.FollowerId == userId && row.CreatedAt > createdAt, ct)
        )
            return false;
        var versions = await db
            .InterestProfiles.AsNoTracking()
            .Where(row => follows.Contains(row.UserId))
            .Select(row => new ProfileVersion(row.UserId, row.UpdatedAt, row.Dirty))
            .ToListAsync(ct);
        return versions.Count == follows.Length
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
        CancellationToken ct
    ) =>
        await db
            .FeedSnapshots.AsNoTracking()
            .Where(row =>
                row.Id == snapshotId && row.UserId == userId && row.Mode == ModeName(mode)
            )
            .Select(row => row.ItemsJson)
            .FirstOrDefaultAsync(ct)
        ?? throw new KeyNotFoundException("Feed snapshot does not exist.");

    public async Task<IReadOnlyList<DiscoveryItem>> GetDigestCandidatesAsync(
        AppDb db,
        Guid userId,
        DigestWindow window,
        DateTimeOffset nowUtc,
        int limit = 10,
        CancellationToken cancellationToken = default
    )
    {
        if (limit is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(limit));
        if (window.EndUtc <= window.StartUtc)
            throw new ArgumentException("Digest window must be non-empty.", nameof(window));
        var now = nowUtc.UtcDateTime;
        var profile = (await GetProfilesAsync(db, [userId], now, cancellationToken))[userId];
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

    public async Task<int> RefreshProfilesAsync(
        AppDb db,
        JobQueue jobs,
        CancellationToken cancellationToken = default,
        int batchSize = 500
    )
    {
        if (batchSize is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(batchSize));
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
                    JsonSerializer.Deserialize<RefreshInterestProfile>(payload, JobQueue.Json) is
                    { } job
                    && job.UserId != Guid.Empty
                )
                    activeIds.Add(job.UserId);
            }
            catch (JsonException) { }
        }

        var slots = Math.Max(0, batchSize - activeIds.Count);
        if (slots == 0)
            return 0;
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db
            .InterestProfiles.AsNoTracking()
            .Where(profile => profile.Dirty || profile.UpdatedAt < now.Date)
            .OrderByDescending(profile => profile.Dirty)
            .ThenBy(profile => profile.UpdatedAt)
            .Select(profile => profile.UserId)
            .Take(batchSize + activeIds.Count)
            .ToListAsync(cancellationToken);
        var queued = 0;
        foreach (var id in due)
        {
            if (!activeIds.Add(id))
                continue;
            jobs.Enqueue(db, new RefreshInterestProfile(id));
            if (++queued == slots)
                break;
        }

        return queued;
    }

    public async Task RefreshProfileAsync(
        AppDb db,
        Guid userId,
        CancellationToken cancellationToken = default
    ) => _ = await GetProfilesAsync(db, [userId], clock.GetUtcNow().UtcDateTime, cancellationToken);

    public async Task<bool> MarkSeenAsync(
        AppDb db,
        Guid userId,
        Guid postId,
        DateTime atUtc,
        CancellationToken cancellationToken = default
    )
    {
        if (!await db.Posts.AnyAsync(post => post.Id == postId, cancellationToken))
            return false;
        var at = DateTime.SpecifyKind(atUtc, DateTimeKind.Utc);
        await db.SaveChangesAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $$"""
            INSERT INTO "SeenPosts" ("UserId", "PostId", "FirstSeenAt", "LastSeenAt") VALUES ({{userId}}, {{postId}}, {{at}}, {{at}})
            ON CONFLICT("UserId", "PostId") DO UPDATE SET "LastSeenAt" =
              CASE WHEN excluded."LastSeenAt" > "SeenPosts"."LastSeenAt" THEN excluded."LastSeenAt" ELSE "SeenPosts"."LastSeenAt" END
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
        var at = DateTime.SpecifyKind(atUtc, DateTimeKind.Utc);
        await db.SaveChangesAsync(cancellationToken);
        await UpsertDirtyProfileAsync(db, userId, at, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    static Task<int> UpsertDirtyProfileAsync(
        AppDb db,
        Guid userId,
        DateTime atUtc,
        CancellationToken ct
    ) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $$"""
            INSERT INTO "InterestProfiles" ("UserId", "TopicsJson", "AuthorsJson", "UpdatedAt", "Dirty") VALUES ({{userId}}, '{}', '{}', {{atUtc}}, 1)
            ON CONFLICT("UserId") DO UPDATE SET "Dirty" = 1,
              "UpdatedAt" = CASE WHEN excluded."UpdatedAt" > "InterestProfiles"."UpdatedAt" THEN excluded."UpdatedAt"
                ELSE strftime('%Y-%m-%d %H:%M:%f', "InterestProfiles"."UpdatedAt", '+0.001 seconds') END
            """,
            ct
        );

    async Task<DiscoveryProfile> GetFeedProfileAsync(
        AppDb db,
        Guid userId,
        DiscoveryFeedMode mode,
        DateTime now,
        CancellationToken ct
    )
    {
        if (mode == DiscoveryFeedMode.ForYou)
            return (await GetProfilesAsync(db, [userId], now, ct))[userId];
        if (mode != DiscoveryFeedMode.FollowingInterests)
            throw new ArgumentOutOfRangeException(nameof(mode));
        var follows = await db
            .Follows.AsNoTracking()
            .Where(follow => follow.FollowerId == userId)
            .OrderBy(follow => follow.FolloweeId)
            .Select(follow => follow.FolloweeId)
            .Take(500)
            .ToListAsync(ct);
        if (follows.Count == 0)
            return EmptyProfile();
        var profiles = await GetProfilesAsync(db, follows, now, ct);
        return RecommendationScoring.MeanNormalizedProfiles(follows.Select(id => profiles[id]));
    }

    async Task<Dictionary<Guid, DiscoveryProfile>> GetProfilesAsync(
        AppDb db,
        IReadOnlyCollection<Guid> userIds,
        DateTime now,
        CancellationToken ct
    )
    {
        var ids = userIds.Distinct().ToArray();
        var result = new Dictionary<Guid, DiscoveryProfile>();
        var versions = new Dictionary<Guid, ProfileVersion>();
        foreach (var batch in ids.Chunk(ProfileBatchSize))
        foreach (
            var row in await db
                .InterestProfiles.AsNoTracking()
                .Where(profile => batch.Contains(profile.UserId))
                .Select(profile => new ProfileVersion(
                    profile.UserId,
                    profile.UpdatedAt,
                    profile.Dirty
                ))
                .ToListAsync(ct)
        )
            versions[row.UserId] = row;
        var rebuild = new List<Guid>();
        var loadJson = new List<Guid>();
        foreach (var id in ids)
        {
            if (
                !versions.TryGetValue(id, out var version)
                || version.Dirty
                || DateTime.SpecifyKind(version.UpdatedAt, DateTimeKind.Utc).Date < now.Date
            )
                rebuild.Add(id);
            else if (readCache is not null)
            {
                var cached = await readCache.TryGetAsync<DiscoveryProfile>(
                    ProfileCacheKey(id, version.UpdatedAt),
                    ct
                );
                if (cached is null)
                    loadJson.Add(id);
                else
                    result[id] = cached;
            }
            else
                loadJson.Add(id);
        }

        foreach (var batch in loadJson.Chunk(ProfileBatchSize))
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
                .ToListAsync(ct);
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
                    await readCache.SetAsync(ProfileCacheKey(id, version.UpdatedAt), profile, ct);
            }
        }

        foreach (var batch in rebuild.Chunk(ProfileBatchSize))
        {
            var computed = await BuildProfilesAsync(db, batch, now, ct);
            foreach (var id in batch)
            {
                var profile = computed[id];
                var topicsJson = JsonSerializer.Serialize(profile.TopicWeights, JsonOptions);
                var authorsJson = JsonSerializer.Serialize(profile.AuthorWeights, JsonOptions);
                if (versions.TryGetValue(id, out var existing))
                {
                    var wrote = await db
                        .InterestProfiles.Where(row =>
                            row.UserId == id && row.UpdatedAt == existing.UpdatedAt
                        )
                        .ExecuteUpdateAsync(
                            set =>
                                set.SetProperty(row => row.TopicsJson, topicsJson)
                                    .SetProperty(row => row.AuthorsJson, authorsJson)
                                    .SetProperty(row => row.UpdatedAt, now)
                                    .SetProperty(row => row.Dirty, false),
                            ct
                        );
                    if (wrote == 0)
                    {
                        var latest = await db
                            .InterestProfiles.AsNoTracking()
                            .FirstOrDefaultAsync(row => row.UserId == id, ct);
                        result[id] =
                            latest is not null && TryDeserializeProfile(latest, out var current)
                                ? current
                                : profile;
                        continue;
                    }

                    if (readCache is not null)
                        await readCache.SetAsync(ProfileCacheKey(id, now), profile, ct);
                }
                else
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $$"""
                        INSERT INTO "InterestProfiles" ("UserId", "TopicsJson", "AuthorsJson", "UpdatedAt", "Dirty")
                        VALUES ({{id}}, {{topicsJson}}, {{authorsJson}}, {{now}}, 0) ON CONFLICT("UserId") DO NOTHING
                        """,
                        ct
                    );
                result[id] = profile;
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
        var output = userIds.ToDictionary(id => id, _ => new List<DiscoverySignal>());
        var since = now - RecommendationScoring.ProfileWindow;
        var postIds = new HashSet<Guid>();
        var likes = await db
            .PostLikes.AsNoTracking()
            .Where(like =>
                userIds.Contains(like.UserId) && like.CreatedAt >= since && like.CreatedAt <= now
            )
            .Select(like => new
            {
                like.UserId,
                like.PostId,
                like.CreatedAt,
            })
            .ToListAsync(ct);
        foreach (var like in likes)
        {
            output[like.UserId]
                .Add(Signal(like.UserId, like.PostId, DiscoverySignalKind.Like, like.CreatedAt));
            postIds.Add(like.PostId);
        }

        var comments = await db
            .Comments.AsNoTracking()
            .Where(comment =>
                userIds.Contains(comment.AuthorId)
                && comment.CreatedAt >= since
                && comment.CreatedAt <= now
            )
            .GroupBy(comment => new { UserId = comment.AuthorId, comment.PostId })
            .Select(group => new
            {
                group.Key.UserId,
                group.Key.PostId,
                At = group.Max(comment => comment.CreatedAt),
            })
            .ToListAsync(ct);
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
                userIds.Contains(seen.UserId) && seen.LastSeenAt >= since && seen.LastSeenAt <= now
            )
            .Select(seen => new
            {
                seen.UserId,
                seen.PostId,
                seen.LastSeenAt,
            })
            .ToListAsync(ct);
        foreach (var view in views)
        {
            output[view.UserId]
                .Add(Signal(view.UserId, view.PostId, DiscoverySignalKind.View, view.LastSeenAt));
            postIds.Add(view.PostId);
        }

        var authors = new Dictionary<Guid, Guid>();
        var published = new Dictionary<Guid, DateTime>();
        var topicsByPost = new Dictionary<Guid, List<string>>();
        foreach (var batch in postIds.Chunk(500))
        {
            var posts = await db
                .Posts.AsNoTracking()
                .Where(post => batch.Contains(post.Id))
                .Select(post => new
                {
                    post.Id,
                    post.AuthorId,
                    post.PublishedAt,
                })
                .ToListAsync(ct);
            foreach (var post in posts)
            {
                authors[post.Id] = post.AuthorId;
                published[post.Id] = post.PublishedAt;
            }

            var topics = await db
                .PostTopics.AsNoTracking()
                .Where(topic => batch.Contains(topic.PostId))
                .Select(topic => new { topic.PostId, topic.Topic })
                .ToListAsync(ct);
            foreach (var topic in topics)
            {
                if (!topicsByPost.TryGetValue(topic.PostId, out var values))
                    topicsByPost[topic.PostId] = values = [];
                values.Add(topic.Topic);
            }
        }

        var postMap = authors.Keys.ToDictionary(
            id => id.ToString(),
            id => new DiscoveryPost(
                id.ToString(),
                authors[id].ToString(),
                topicsByPost.GetValueOrDefault(id) ?? [],
                new DateTimeOffset(DateTime.SpecifyKind(published[id], DateTimeKind.Utc))
            ),
            StringComparer.Ordinal
        );
        var at = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));
        return output.ToDictionary(
            pair => pair.Key,
            pair => RecommendationScoring.BuildProfile(pair.Key.ToString(), pair.Value, postMap, at)
        );
    }

    async Task<IReadOnlyList<DiscoveryItem>> BuildFeedItemsAsync(
        AppDb db,
        Guid userId,
        DiscoveryFeedMode mode,
        DiscoveryProfile profile,
        DateTime now,
        CancellationToken ct
    )
    {
        if (!profile.HasSignals)
            return mode == DiscoveryFeedMode.FollowingInterests
                ? await BuildColdStartFollowingItemsAsync(db, userId, ct)
                : [];
        var candidates = await LoadCandidatesAsync(
            db,
            userId,
            profile,
            now - RecommendationScoring.ProfileWindow,
            null,
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
        CancellationToken ct
    )
    {
        var follows = await db
            .Follows.AsNoTracking()
            .Where(follow => follow.FollowerId == userId)
            .Select(follow => follow.FolloweeId)
            .Take(500)
            .ToArrayAsync(ct);
        if (follows.Length == 0)
            return [];
        var reposts = db
            .Reposts.AsNoTracking()
            .Where(repost => follows.Contains(repost.UserId))
            .Select(repost => repost.PostId);
        var rows = await db
            .Posts.AsNoTracking()
            .Where(post =>
                post.AuthorId != userId
                && (follows.Contains(post.AuthorId) || reposts.Contains(post.Id))
                && !db.SeenPosts.Any(seen => seen.UserId == userId && seen.PostId == post.Id)
            )
            .OrderByDescending(post => post.PublishedAt)
            .ThenByDescending(post => post.Id)
            .Take(MaximumSnapshotItems)
            .Select(post => new { post.Id })
            .ToListAsync(ct);
        return rows.Select(row => new DiscoveryItem(
                row.Id,
                RecommendationScoring.MinimumAffinity,
                "From someone you follow"
            ))
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
            .AuthorWeights.Keys.Select(id => Guid.TryParse(id, out var author) ? author : Guid.Empty)
            .Where(id => id != Guid.Empty)
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
            .ToListAsync(ct);
        var ids = rows.Select(row => row.Id).ToArray();
        var topicRows =
            ids.Length == 0
                ? []
                : await db
                    .PostTopics.AsNoTracking()
                    .Where(topic => ids.Contains(topic.PostId))
                    .Select(topic => new { topic.PostId, topic.Topic })
                    .ToListAsync(ct);
        var topics = topicRows
            .GroupBy(topic => topic.PostId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<string>)group.Select(x => x.Topic).ToArray()
            );
        return rows.Select(row => new DiscoveryPost(
                row.Id.ToString(),
                row.AuthorId.ToString(),
                topics.GetValueOrDefault(row.Id) ?? [],
                new DateTimeOffset(DateTime.SpecifyKind(row.PublishedAt, DateTimeKind.Utc))
            ))
            .ToList();
    }
}
