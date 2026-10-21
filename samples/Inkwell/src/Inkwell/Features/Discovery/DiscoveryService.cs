using System.Globalization;
using System.Text.Json;
using Inkwell.Data;
using Inkwell.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Features.Discovery;

/// <summary>Builds stable ranked feed snapshots and keeps each user's sparse interest profile current.</summary>
public sealed class DiscoveryService(AppDb db, JobQueue jobs, TimeProvider time)
{
    const int MaximumFeedCandidates = 2_000;
    const int MaximumSnapshotItems = 1_000;
    const int MaximumFollowees = 500;
    static readonly TimeSpan SnapshotLifetime = TimeSpan.FromMinutes(30);
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    sealed record ProfileVersion(DateTime UpdatedAt, bool Dirty);

    DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// One page of a user's ranked feed. Without a cursor it reuses the latest snapshot while it is still current, or
    /// builds a new one; with a cursor it pages through that snapshot. Throws <see cref="ArgumentException"/> for a bad
    /// cursor and <see cref="KeyNotFoundException"/> when the snapshot is gone or expired.
    /// </summary>
    public async Task<DiscoveryPage> GetPageAsync(Guid userId, DiscoveryFeedMode mode, string? cursor, int pageSize, CancellationToken ct = default)
    {
        var modeName = ModeName(mode);
        var now = Now;
        string snapshotId;
        IReadOnlyList<DiscoveryItem> all;
        var offset = 0;
        if (cursor is null)
        {
            var latest = await db.FeedSnapshots.AsNoTracking()
                .Where(s => s.UserId == userId && s.Mode == modeName && s.ExpiresAt > now)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new { s.Id, s.CreatedAt })
                .FirstOrDefaultAsync(ct);
            if (latest is not null && await IsSnapshotCurrentAsync(userId, mode, latest.CreatedAt, now, ct))
            {
                snapshotId = latest.Id;
                // A follow change can delete the snapshot between the two reads; report it like an expired cursor.
                all = Deserialize(await db.FeedSnapshots.AsNoTracking().Where(s => s.Id == latest.Id).Select(s => s.ItemsJson).FirstOrDefaultAsync(ct)
                    ?? throw new KeyNotFoundException("Feed snapshot does not exist."));
            }
            else
            {
                var profile = await GetFeedProfileAsync(userId, mode, now, ct);
                all = await BuildFeedItemsAsync(userId, mode, profile, now, ct);
                var snapshot = new FeedSnapshot
                {
                    UserId = userId,
                    Mode = modeName,
                    ItemsJson = JsonSerializer.Serialize(all, Json),
                    CreatedAt = now,
                    ExpiresAt = now + SnapshotLifetime,
                };
                db.FeedSnapshots.Add(snapshot);
                await db.SaveChangesAsync(ct);
                snapshotId = snapshot.Id;
            }
        }
        else
        {
            (snapshotId, offset) = ParseCursor(cursor);
            var id = snapshotId;
            var stored = await db.FeedSnapshots.AsNoTracking()
                .Where(s => s.Id == id && s.UserId == userId && s.Mode == modeName)
                .Select(s => new { s.ExpiresAt, s.ItemsJson })
                .FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("Feed snapshot does not exist.");
            if (stored.ExpiresAt <= now) throw new KeyNotFoundException("Feed snapshot has expired.");
            all = Deserialize(stored.ItemsJson);
        }

        if (offset > all.Count) throw new ArgumentException("Feed cursor is outside the snapshot.", nameof(cursor));
        var rawPage = all.Skip(offset).Take(pageSize).ToArray();
        var ids = rawPage.Select(i => i.PostId).ToArray();
        // Posts seen after the snapshot was built drop out of later pages.
        var unseen = ids.Length == 0
            ? []
            : (await db.Posts.AsNoTracking()
                .Where(p => ids.Contains(p.Id) && !db.SeenPosts.Any(s => s.UserId == userId && s.PostId == p.Id))
                .Select(p => p.Id)
                .ToListAsync(ct)).ToHashSet();
        var next = offset + rawPage.Length;
        var hasMore = next < all.Count;
        return new DiscoveryPage(rawPage.Where(i => unseen.Contains(i.PostId)).ToArray(), hasMore ? $"{snapshotId}.{next}" : null, hasMore);
    }

    public async Task<IReadOnlyList<DiscoveryItem>> GetDigestCandidatesAsync(
        Guid userId, DateTime fromUtc, DateTime toUtc, int limit, CancellationToken ct = default)
    {
        var now = Now;
        var profile = (await GetProfilesAsync([userId], now, ct))[userId];
        if (!profile.HasSignals) return [];
        var candidates = await LoadCandidatesAsync(userId, profile, fromUtc, toUtc, MaximumFeedCandidates, ct);
        return RankAndDescribe(candidates, userId, profile, now).Take(limit).ToArray();
    }

    /// <summary>Queues refresh jobs for dirty or day-old profiles, skipping users that already have one pending.</summary>
    public async Task<int> RefreshProfilesAsync(int batchSize = 500, CancellationToken ct = default)
    {
        var payloads = await db.Jobs.AsNoTracking()
            .Where(j => j.Type == nameof(RefreshInterestProfile) && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running))
            .Select(j => j.Payload)
            .ToListAsync(ct);
        var active = payloads
            .Select(p => JsonSerializer.Deserialize<RefreshInterestProfile>(p, JobQueue.Json)?.UserId ?? Guid.Empty)
            .ToHashSet();
        var slots = batchSize - active.Count;
        if (slots <= 0) return 0;

        var today = Now.Date;
        var due = await db.InterestProfiles.AsNoTracking()
            .Where(p => p.Dirty || p.UpdatedAt < today)
            .OrderByDescending(p => p.Dirty).ThenBy(p => p.UpdatedAt)
            .Select(p => p.UserId)
            .Take(batchSize + active.Count)
            .ToListAsync(ct);
        var queued = 0;
        foreach (var id in due.Where(active.Add).Take(slots))
        {
            jobs.Enqueue(db, new RefreshInterestProfile(id));
            queued++;
        }
        return queued;
    }

    public async Task RefreshProfileAsync(Guid userId, CancellationToken ct = default) =>
        await GetProfilesAsync([userId], Now, ct);

    /// <summary>Records that the user saw the post (the latest time wins) and marks their profile dirty.</summary>
    public async Task MarkSeenAsync(Guid userId, Guid postId, DateTime atUtc, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "SeenPosts" ("UserId", "PostId", "FirstSeenAt", "LastSeenAt") VALUES ({{userId}}, {{postId}}, {{atUtc}}, {{atUtc}})
            ON CONFLICT("UserId", "PostId") DO UPDATE SET "LastSeenAt" = MAX(excluded."LastSeenAt", "SeenPosts"."LastSeenAt")
            """, ct);
        await MarkProfileDirtyAsync(userId, atUtc, ct);
    }

    /// <summary>Flags the profile for rebuild. UpdatedAt always moves forward so an in-flight rebuild loses its compare-and-swap.</summary>
    public Task MarkProfileDirtyAsync(Guid userId, DateTime atUtc, CancellationToken ct = default) =>
        db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "InterestProfiles" ("UserId", "TopicsJson", "AuthorsJson", "UpdatedAt", "Dirty") VALUES ({{userId}}, '{}', '{}', {{atUtc}}, 1)
            ON CONFLICT("UserId") DO UPDATE SET "Dirty" = 1,
              "UpdatedAt" = CASE WHEN excluded."UpdatedAt" > "InterestProfiles"."UpdatedAt" THEN excluded."UpdatedAt"
                ELSE strftime('%Y-%m-%d %H:%M:%f', "InterestProfiles"."UpdatedAt", '+0.001 seconds') END
            """, ct);

    async Task<bool> IsSnapshotCurrentAsync(Guid userId, DiscoveryFeedMode mode, DateTime snapshotCreatedAt, DateTime now, CancellationToken ct)
    {
        if (mode == DiscoveryFeedMode.ForYou)
        {
            var own = await db.InterestProfiles.AsNoTracking().Where(p => p.UserId == userId)
                .Select(p => new ProfileVersion(p.UpdatedAt, p.Dirty))
                .FirstOrDefaultAsync(ct);
            return own is not null && IsFresh(own, snapshotCreatedAt, now);
        }

        var follows = await FolloweesAsync(userId, ct);
        if (follows.Length == 0) return true;
        if (await db.Follows.AsNoTracking().AnyAsync(f => f.FollowerId == userId && f.CreatedAt > snapshotCreatedAt, ct))
            return false;
        var versions = await db.InterestProfiles.AsNoTracking().Where(p => follows.Contains(p.UserId))
            .Select(p => new ProfileVersion(p.UpdatedAt, p.Dirty))
            .ToListAsync(ct);
        return versions.Count == follows.Length && versions.All(v => IsFresh(v, snapshotCreatedAt, now));
    }

    /// <summary>A profile is reusable when clean, built no later than the snapshot and built today.</summary>
    static bool IsFresh(ProfileVersion v, DateTime snapshotCreatedAt, DateTime now) =>
        !v.Dirty && v.UpdatedAt <= snapshotCreatedAt && v.UpdatedAt.Date >= now.Date;

    Task<Guid[]> FolloweesAsync(Guid userId, CancellationToken ct) =>
        db.Follows.AsNoTracking().Where(f => f.FollowerId == userId)
            .OrderBy(f => f.FolloweeId).Select(f => f.FolloweeId)
            .Take(MaximumFollowees)
            .ToArrayAsync(ct);

    async Task<DiscoveryProfile> GetFeedProfileAsync(Guid userId, DiscoveryFeedMode mode, DateTime now, CancellationToken ct)
    {
        if (mode == DiscoveryFeedMode.ForYou)
            return (await GetProfilesAsync([userId], now, ct))[userId];
        var follows = await FolloweesAsync(userId, ct);
        if (follows.Length == 0) return RecommendationScoring.Empty;
        var profiles = await GetProfilesAsync(follows, now, ct);
        return RecommendationScoring.MeanNormalizedProfiles(follows.Select(id => profiles[id]));
    }

    /// <summary>Stored profiles when clean and built today, rebuilt (and stored) otherwise.</summary>
    async Task<Dictionary<Guid, DiscoveryProfile>> GetProfilesAsync(IReadOnlyCollection<Guid> userIds, DateTime now, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToArray();
        var stored = await db.InterestProfiles.AsNoTracking().Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, ct);
        var result = new Dictionary<Guid, DiscoveryProfile>();
        var rebuild = new List<Guid>();
        foreach (var id in ids)
        {
            if (stored.TryGetValue(id, out var row) && !row.Dirty && row.UpdatedAt.Date >= now.Date && TryParse(row, out var profile))
                result[id] = profile;
            else
                rebuild.Add(id);
        }
        if (rebuild.Count == 0) return result;

        var built = await BuildProfilesAsync(rebuild, now, ct);
        foreach (var id in rebuild)
        {
            var profile = built[id];
            var topics = JsonSerializer.Serialize(profile.TopicWeights, Json);
            var authors = JsonSerializer.Serialize(profile.AuthorWeights, Json);
            result[id] = profile;
            if (!stored.TryGetValue(id, out var existing))
            {
                await db.Database.ExecuteSqlInterpolatedAsync($$"""
                    INSERT INTO "InterestProfiles" ("UserId", "TopicsJson", "AuthorsJson", "UpdatedAt", "Dirty")
                    VALUES ({{id}}, {{topics}}, {{authors}}, {{now}}, 0) ON CONFLICT("UserId") DO NOTHING
                    """, ct);
                continue;
            }

            // Compare-and-swap on UpdatedAt: a signal recorded while this rebuild ran keeps the profile dirty.
            var written = await db.InterestProfiles.Where(p => p.UserId == id && p.UpdatedAt == existing.UpdatedAt)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.TopicsJson, topics).SetProperty(p => p.AuthorsJson, authors)
                    .SetProperty(p => p.UpdatedAt, now).SetProperty(p => p.Dirty, false), ct);
            if (written == 0)
            {
                var latest = await db.InterestProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == id, ct);
                if (latest is not null && TryParse(latest, out var current)) result[id] = current;
            }
        }
        return result;
    }

    async Task<Dictionary<Guid, DiscoveryProfile>> BuildProfilesAsync(IReadOnlyCollection<Guid> userIds, DateTime now, CancellationToken ct)
    {
        var since = now - RecommendationScoring.ProfileWindow;
        var signals = new List<DiscoverySignal>();

        signals.AddRange((await db.PostLikes.AsNoTracking()
                .Where(l => userIds.Contains(l.UserId) && l.CreatedAt >= since && l.CreatedAt <= now)
                .Select(l => new { l.UserId, l.PostId, At = l.CreatedAt })
                .ToListAsync(ct))
            .Select(l => Signal(l.UserId, l.PostId, DiscoverySignalKind.Like, l.At)));

        signals.AddRange((await db.Comments.AsNoTracking()
                .Where(c => userIds.Contains(c.AuthorId) && c.CreatedAt >= since && c.CreatedAt <= now)
                .GroupBy(c => new { UserId = c.AuthorId, c.PostId })
                .Select(g => new { g.Key.UserId, g.Key.PostId, At = g.Max(c => c.CreatedAt) })
                .ToListAsync(ct))
            .Select(c => Signal(c.UserId, c.PostId, DiscoverySignalKind.Comment, c.At)));

        signals.AddRange((await db.SeenPosts.AsNoTracking()
                .Where(s => userIds.Contains(s.UserId) && s.LastSeenAt >= since && s.LastSeenAt <= now)
                .Select(s => new { s.UserId, s.PostId, At = s.LastSeenAt })
                .ToListAsync(ct))
            .Select(s => Signal(s.UserId, s.PostId, DiscoverySignalKind.View, s.At)));

        var posts = new Dictionary<string, DiscoveryPost>(StringComparer.Ordinal);
        foreach (var batch in signals.Select(s => Guid.Parse(s.PostId)).Distinct().Chunk(500))
        {
            var rows = await db.Posts.AsNoTracking().Where(p => batch.Contains(p.Id))
                .Select(p => new { p.Id, p.AuthorId, p.PublishedAt })
                .ToListAsync(ct);
            var topics = (await db.PostTopics.AsNoTracking().Where(t => batch.Contains(t.PostId)).ToListAsync(ct))
                .ToLookup(t => t.PostId, t => t.Topic);
            foreach (var p in rows)
                posts[p.Id.ToString()] = new DiscoveryPost(p.Id.ToString(), p.AuthorId.ToString(), topics[p.Id].ToArray(), Utc(p.PublishedAt));
        }

        var at = Utc(now);
        return userIds.ToDictionary(id => id, id => RecommendationScoring.BuildProfile(id.ToString(), signals, posts, at));
    }

    async Task<IReadOnlyList<DiscoveryItem>> BuildFeedItemsAsync(Guid userId, DiscoveryFeedMode mode, DiscoveryProfile profile, DateTime now, CancellationToken ct)
    {
        if (!profile.HasSignals)
            return mode == DiscoveryFeedMode.FollowingInterests ? await ColdStartFollowingAsync(userId, ct) : [];
        var candidates = await LoadCandidatesAsync(userId, profile, now - RecommendationScoring.ProfileWindow, null, MaximumFeedCandidates, ct);
        return RankAndDescribe(candidates, userId, profile, now).Take(MaximumSnapshotItems).ToArray();
    }

    /// <summary>With nothing to score yet, "Following" is simply the newest unseen posts of followed people and their reposts.</summary>
    async Task<IReadOnlyList<DiscoveryItem>> ColdStartFollowingAsync(Guid userId, CancellationToken ct)
    {
        var follows = await FolloweesAsync(userId, ct);
        if (follows.Length == 0) return [];
        var reposted = db.Reposts.AsNoTracking().Where(r => follows.Contains(r.UserId)).Select(r => r.PostId);
        var ids = await db.Posts.AsNoTracking()
            .Where(p => p.AuthorId != userId
                && (follows.Contains(p.AuthorId) || reposted.Contains(p.Id))
                && !db.SeenPosts.Any(s => s.UserId == userId && s.PostId == p.Id))
            .OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id)
            .Select(p => p.Id)
            .Take(MaximumSnapshotItems)
            .ToListAsync(ct);
        return ids.Select(id => new DiscoveryItem(id, RecommendationScoring.MinimumAffinity, "From someone you follow")).ToArray();
    }

    async Task<List<DiscoveryPost>> LoadCandidatesAsync(Guid userId, DiscoveryProfile profile, DateTime fromUtc, DateTime? toUtcExclusive, int maximum, CancellationToken ct)
    {
        var topicIds = profile.TopicWeights.Keys.ToArray();
        var authorIds = profile.AuthorWeights.Keys.Select(Guid.Parse).ToArray();
        if (topicIds.Length == 0 && authorIds.Length == 0) return [];
        var rows = await db.Posts.AsNoTracking()
            .Where(p => p.AuthorId != userId
                && p.PublishedAt >= fromUtc
                && (toUtcExclusive == null || p.PublishedAt < toUtcExclusive)
                && !db.SeenPosts.Any(s => s.UserId == userId && s.PostId == p.Id)
                && (authorIds.Contains(p.AuthorId) || db.PostTopics.Any(t => t.PostId == p.Id && topicIds.Contains(t.Topic))))
            .OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id)
            .Take(maximum)
            .Select(p => new { p.Id, p.AuthorId, p.PublishedAt })
            .ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToArray();
        var topics = (await db.PostTopics.AsNoTracking().Where(t => ids.Contains(t.PostId)).ToListAsync(ct))
            .ToLookup(t => t.PostId, t => t.Topic);
        return rows.Select(r => new DiscoveryPost(r.Id.ToString(), r.AuthorId.ToString(), topics[r.Id].ToArray(), Utc(r.PublishedAt))).ToList();
    }

    static IEnumerable<DiscoveryItem> RankAndDescribe(IReadOnlyCollection<DiscoveryPost> candidates, Guid viewerId, DiscoveryProfile profile, DateTime now) =>
        RecommendationScoring.Rank(candidates, viewerId.ToString(), profile, Utc(now))
            .Select(item => new DiscoveryItem(Guid.Parse(item.Post.Id), item.Score, Reason(item.Post, profile)));

    static string Reason(DiscoveryPost post, DiscoveryProfile profile)
    {
        var topic = post.TopicIds.Any(profile.TopicWeights.ContainsKey);
        var author = profile.AuthorWeights.ContainsKey(post.AuthorId);
        if (topic && author) return "Matches your topics and authors";
        return topic ? "Matches your topics" : "Matches an author you engage with";
    }

    static bool TryParse(InterestProfile row, out DiscoveryProfile profile)
    {
        try
        {
            profile = new DiscoveryProfile(
                JsonSerializer.Deserialize<Dictionary<string, double>>(row.TopicsJson, Json) ?? [],
                JsonSerializer.Deserialize<Dictionary<string, double>>(row.AuthorsJson, Json) ?? []);
            return true;
        }
        catch (JsonException)
        {
            profile = RecommendationScoring.Empty;
            return false;
        }
    }

    static DiscoverySignal Signal(Guid userId, Guid postId, DiscoverySignalKind kind, DateTime at) =>
        new(userId.ToString(), postId.ToString(), kind, Utc(at));

    static DateTimeOffset Utc(DateTime at) => new(DateTime.SpecifyKind(at, DateTimeKind.Utc));

    static string ModeName(DiscoveryFeedMode mode) => mode == DiscoveryFeedMode.ForYou ? "for-you" : "following";

    static IReadOnlyList<DiscoveryItem> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<DiscoveryItem>>(json, Json) ?? throw new InvalidDataException("Feed snapshot has no item list.");

    /// <summary>Cursors are "{snapshotId}.{offset}", with the snapshot id in 32-digit hex form.</summary>
    static (string SnapshotId, int Offset) ParseCursor(string value)
    {
        var dot = value.LastIndexOf('.');
        if (value.Length > 80 || dot <= 0
            || !Guid.TryParseExact(value[..dot], "N", out _)
            || !int.TryParse(value[(dot + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
            || offset < 1)
            throw new ArgumentException("Invalid feed cursor.", nameof(value));
        return (value[..dot], offset);
    }
}
