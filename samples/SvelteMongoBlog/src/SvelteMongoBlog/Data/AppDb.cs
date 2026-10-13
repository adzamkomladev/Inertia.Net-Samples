using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace SvelteMongoBlog.Data;

/// <summary>Mongo collections and indexes used by this sample. Writes that span collections use transactions in MONGO_MODE=replica and run best effort in MONGO_MODE=single.</summary>
public sealed class AppDb
{
    // Ids are UUIDv7 values assigned in app code; Standard (subtype 4) is the portable Guid wire format.
    static AppDb()
    {
        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        BsonSerializer.RegisterSerializer(new NullableSerializer<Guid>(new GuidSerializer(GuidRepresentation.Standard)));
    }

    readonly IMongoClient client;
    readonly IMongoDatabase database;
    readonly MongoMode mode;

    public AppDb(IMongoClient client, IConfiguration configuration)
    {
        this.client = client;
        mode = MongoModeConfig.Read(configuration);
        var url = new MongoUrl(configuration["DATABASE_URL"] ?? configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("DATABASE_URL must be configured."));
        var name = configuration["DATABASE_NAME"] ?? url.DatabaseName ?? "svelte_mongo_blog";
        database = client.GetDatabase(name);
        Users = database.GetCollection<User>("users");
        Posts = database.GetCollection<Post>("posts");
        Comments = database.GetCollection<Comment>("comments");
        Likes = database.GetCollection<Like>("likes");
        Assets = database.GetCollection<Asset>("assets");
        PostViews = database.GetCollection<PostView>("post_views");
        PostDailyStats = database.GetCollection<PostDailyStat>("post_daily_stats");
        Jobs = database.GetCollection<Job>("jobs");
        CronLeases = database.GetCollection<CronLease>("cron_leases");
        PostTopics = database.GetCollection<PostTopic>("post_topics");
        Follows = database.GetCollection<Follow>("follows");
        Bookmarks = database.GetCollection<Bookmark>("bookmarks");
        CommentLikes = database.GetCollection<CommentLike>("comment_likes");
        Reposts = database.GetCollection<Repost>("reposts");
        SeenPosts = database.GetCollection<SeenPost>("seen_posts");
        InterestProfiles = database.GetCollection<InterestProfile>("interest_profiles");
        FeedSnapshots = database.GetCollection<FeedSnapshot>("feed_snapshots");
        DigestDeliveries = database.GetCollection<DigestDelivery>("digest_deliveries");
        SearchIndexStates = database.GetCollection<SearchIndexState>("search_index_states");
        SeedCheckpoints = database.GetCollection<SeedCheckpoint>("seed_checkpoints");
    }

    public IMongoCollection<User> Users { get; }
    public IMongoCollection<Post> Posts { get; }
    public IMongoCollection<Comment> Comments { get; }
    public IMongoCollection<Like> Likes { get; }
    public IMongoCollection<Asset> Assets { get; }
    public IMongoCollection<PostView> PostViews { get; }
    public IMongoCollection<PostDailyStat> PostDailyStats { get; }
    public IMongoCollection<Job> Jobs { get; }
    public IMongoCollection<CronLease> CronLeases { get; }
    public IMongoDatabase Database => database;
    public IMongoCollection<PostTopic> PostTopics { get; }
    public IMongoCollection<Follow> Follows { get; }
    public IMongoCollection<Bookmark> Bookmarks { get; }
    public IMongoCollection<CommentLike> CommentLikes { get; }
    public IMongoCollection<Repost> Reposts { get; }
    public IMongoCollection<SeenPost> SeenPosts { get; }
    public IMongoCollection<InterestProfile> InterestProfiles { get; }
    public IMongoCollection<FeedSnapshot> FeedSnapshots { get; }
    public IMongoCollection<DigestDelivery> DigestDeliveries { get; }
    public IMongoCollection<SearchIndexState> SearchIndexStates { get; }
    public IMongoCollection<SeedCheckpoint> SeedCheckpoints { get; }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
        await Users.Indexes.CreateOneAsync(new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(x => x.Email), new CreateIndexOptions { Unique = true, Name = "email_unique" }), cancellationToken: ct);
        await Posts.Indexes.CreateManyAsync([
            new CreateIndexModel<Post>(Builders<Post>.IndexKeys.Ascending(x => x.Slug), new CreateIndexOptions { Unique = true, Name = "slug_unique" }),
            new CreateIndexModel<Post>(Builders<Post>.IndexKeys.Descending(x => x.CreatedAt).Descending(x => x.Id), new CreateIndexOptions { Name = "timeline_cursor" }),
            new CreateIndexModel<Post>(Builders<Post>.IndexKeys.Ascending(x => x.AuthorId).Descending(x => x.CreatedAt), new CreateIndexOptions { Name = "author_timeline" }),
            new CreateIndexModel<Post>(Builders<Post>.IndexKeys.Ascending(x => x.AuthorId).Descending(x => x.CreatedAt).Descending(x => x.Id), new CreateIndexOptions { Name = "author_timeline_cursor" }),
            new CreateIndexModel<Post>(Builders<Post>.IndexKeys.Text(x => x.Title).Text(x => x.Body), new CreateIndexOptions { Name = "post_text" })
        ], cancellationToken: ct);
        await Comments.Indexes.CreateOneAsync(new CreateIndexModel<Comment>(Builders<Comment>.IndexKeys.Ascending(x => x.PostId).Descending(x => x.CreatedAt).Descending(x => x.Id), new CreateIndexOptions { Name = "post_comments" }), cancellationToken: ct);
        await Likes.Indexes.CreateManyAsync([
            new CreateIndexModel<Like>(Builders<Like>.IndexKeys.Ascending(x => x.PostId).Ascending(x => x.UserId), new CreateIndexOptions { Unique = true, Name = "post_user_unique" }),
            new CreateIndexModel<Like>(Builders<Like>.IndexKeys.Ascending(x => x.PostId).Descending(x => x.CreatedAt), new CreateIndexOptions { Name = "post_likes" })
        ], cancellationToken: ct);
        await Assets.Indexes.CreateManyAsync([
            new CreateIndexModel<Asset>(Builders<Asset>.IndexKeys.Ascending(x => x.Key), new CreateIndexOptions { Unique = true, Name = "asset_key_unique" }),
            new CreateIndexModel<Asset>(Builders<Asset>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.CreatedAt), new CreateIndexOptions { Name = "asset_cleanup" })
        ], cancellationToken: ct);
        await PostViews.Indexes.CreateManyAsync([
            new CreateIndexModel<PostView>(Builders<PostView>.IndexKeys.Ascending(x => x.PostId).Ascending(x => x.At), new CreateIndexOptions { Name = "post_views_at" }),
            new CreateIndexModel<PostView>(Builders<PostView>.IndexKeys.Ascending(x => x.At), new CreateIndexOptions { Name = "views_prune" })
        ], cancellationToken: ct);
        await PostDailyStats.Indexes.CreateOneAsync(new CreateIndexModel<PostDailyStat>(Builders<PostDailyStat>.IndexKeys.Ascending(x => x.PostId).Ascending(x => x.Date), new CreateIndexOptions { Unique = true, Name = "post_day_unique" }), cancellationToken: ct);
        await Jobs.Indexes.CreateManyAsync([
            new CreateIndexModel<Job>(Builders<Job>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.RunAt).Ascending(x => x.Id), new CreateIndexOptions { Name = "jobs_claim" }),
            new CreateIndexModel<Job>(Builders<Job>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.LockedUntil), new CreateIndexOptions { Name = "jobs_lease" })
        ], cancellationToken: ct);
        await Users.Indexes.CreateManyAsync([
            new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(x => x.DemoKey), new CreateIndexOptions { Unique = true, Sparse = true, Name = "demo_key_unique" }),
            new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(x => x.NextDigestAtUtc), new CreateIndexOptions { Name = "daily_digest_due" })
        ], cancellationToken: ct);
        await Posts.Indexes.CreateManyAsync([
            new CreateIndexModel<Post>(Builders<Post>.IndexKeys.Ascending(x => x.DemoKey), new CreateIndexOptions { Unique = true, Sparse = true, Name = "demo_key_unique" }),
            new CreateIndexModel<Post>(Builders<Post>.IndexKeys.Descending(x => x.PublishedAt).Descending(x => x.Id), new CreateIndexOptions { Name = "published_cursor" }),
            new CreateIndexModel<Post>(Builders<Post>.IndexKeys.Ascending(x => x.ArchivedAt).Descending(x => x.PublishedAt), new CreateIndexOptions { Name = "public_published" }),
            new CreateIndexModel<Post>(Builders<Post>.IndexKeys.Ascending(x => x.AuthorId).Ascending(x => x.ArchivedAt).Descending(x => x.PublishedAt).Descending(x => x.Id), new CreateIndexOptions { Name = "author_published_candidates" })
        ], cancellationToken: ct);
        await PostTopics.Indexes.CreateManyAsync([
            new CreateIndexModel<PostTopic>(Builders<PostTopic>.IndexKeys.Ascending(x => x.Topic).Ascending(x => x.PostId), new CreateIndexOptions { Name = "topic_posts" }),
            new CreateIndexModel<PostTopic>(Builders<PostTopic>.IndexKeys.Ascending(x => x.PostId).Ascending(x => x.Topic), new CreateIndexOptions { Unique = true, Name = "post_topic_unique" })
        ], cancellationToken: ct);
        await ReplaceLegacyNonUniqueFollowIndexAsync(ct);
        await Follows.Indexes.CreateManyAsync([
            new CreateIndexModel<Follow>(Builders<Follow>.IndexKeys.Ascending(x => x.FolloweeId).Ascending(x => x.FollowerId), new CreateIndexOptions { Name = "followers" }),
            new CreateIndexModel<Follow>(Builders<Follow>.IndexKeys.Ascending(x => x.FollowerId).Ascending(x => x.FolloweeId), new CreateIndexOptions { Unique = true, Name = "followees_by_follower" })
        ], cancellationToken: ct);
        await Bookmarks.Indexes.CreateManyAsync([
            new CreateIndexModel<Bookmark>(Builders<Bookmark>.IndexKeys.Ascending(x => x.UserId).Descending(x => x.CreatedAt).Descending(x => x.PostId), new CreateIndexOptions { Name = "user_bookmark_timeline" }),
            new CreateIndexModel<Bookmark>(Builders<Bookmark>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.PostId), new CreateIndexOptions { Unique = true, Name = "user_post_unique" })
        ], cancellationToken: ct);
        await Reposts.Indexes.CreateManyAsync([
            new CreateIndexModel<Repost>(Builders<Repost>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.PostId), new CreateIndexOptions { Unique = true, Name = "user_post_unique" }),
            new CreateIndexModel<Repost>(Builders<Repost>.IndexKeys.Ascending(x => x.UserId).Descending(x => x.CreatedAt).Descending(x => x.Id), new CreateIndexOptions { Name = "user_repost_timeline" }),
            new CreateIndexModel<Repost>(Builders<Repost>.IndexKeys.Ascending(x => x.UserId).Descending(x => x.CreatedAt).Descending(x => x.PostId), new CreateIndexOptions { Name = "user_repost_cursor" }),
            new CreateIndexModel<Repost>(Builders<Repost>.IndexKeys.Ascending(x => x.PostId).Descending(x => x.CreatedAt), new CreateIndexOptions { Name = "post_repost_timeline" })
        ], cancellationToken: ct);
        await SeenPosts.Indexes.CreateManyAsync([
            new CreateIndexModel<SeenPost>(Builders<SeenPost>.IndexKeys.Ascending(x => x.UserId).Descending(x => x.LastSeenAt), new CreateIndexOptions { Name = "user_seen_recent" }),
            new CreateIndexModel<SeenPost>(Builders<SeenPost>.IndexKeys.Ascending(x => x.PostId).Ascending(x => x.UserId), new CreateIndexOptions { Name = "post_seen_users" }),
            new CreateIndexModel<SeenPost>(Builders<SeenPost>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.PostId), new CreateIndexOptions { Unique = true, Name = "user_post_unique" })
        ], cancellationToken: ct);
        await CommentLikes.Indexes.CreateOneAsync(new CreateIndexModel<CommentLike>(Builders<CommentLike>.IndexKeys.Ascending(x => x.CommentId).Ascending(x => x.UserId), new CreateIndexOptions { Unique = true, Name = "comment_user_unique" }), cancellationToken: ct);
        await Likes.Indexes.CreateOneAsync(new CreateIndexModel<Like>(Builders<Like>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.CreatedAt), new CreateIndexOptions { Name = "user_interest_signals" }), cancellationToken: ct);
        await Comments.Indexes.CreateOneAsync(new CreateIndexModel<Comment>(Builders<Comment>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.CreatedAt), new CreateIndexOptions { Name = "user_interest_signals" }), cancellationToken: ct);
        await Comments.Indexes.CreateOneAsync(new CreateIndexModel<Comment>(Builders<Comment>.IndexKeys.Ascending(x => x.CreatedAt), new CreateIndexOptions { Name = "daily_stats_window" }), cancellationToken: ct);
        await Likes.Indexes.CreateOneAsync(new CreateIndexModel<Like>(Builders<Like>.IndexKeys.Ascending(x => x.CreatedAt), new CreateIndexOptions { Name = "daily_stats_window" }), cancellationToken: ct);
        await Posts.Indexes.CreateOneAsync(new CreateIndexModel<Post>(Builders<Post>.IndexKeys.Ascending(x => x.CoverAssetId), new CreateIndexOptions { Name = "cover_asset" }), cancellationToken: ct);
        await Users.Indexes.CreateOneAsync(new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(x => x.AvatarAssetId), new CreateIndexOptions { Name = "avatar_asset" }), cancellationToken: ct);
        await InterestProfiles.Indexes.CreateOneAsync(new CreateIndexModel<InterestProfile>(Builders<InterestProfile>.IndexKeys.Ascending(x => x.Dirty).Ascending(x => x.UpdatedAt), new CreateIndexOptions { Name = "dirty_profiles" }), cancellationToken: ct);
        await FeedSnapshots.Indexes.CreateManyAsync([
            new CreateIndexModel<FeedSnapshot>(Builders<FeedSnapshot>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.Mode).Descending(x => x.CreatedAt), new CreateIndexOptions { Name = "user_feed_snapshots" }),
            new CreateIndexModel<FeedSnapshot>(Builders<FeedSnapshot>.IndexKeys.Ascending(x => x.ExpiresAt), new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "snapshot_ttl" })
        ], cancellationToken: ct);
        await DigestDeliveries.Indexes.CreateManyAsync([
            new CreateIndexModel<DigestDelivery>(Builders<DigestDelivery>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.LocalDate), new CreateIndexOptions { Unique = true, Name = "user_local_date_unique" }),
            new CreateIndexModel<DigestDelivery>(Builders<DigestDelivery>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.CreatedAt), new CreateIndexOptions { Name = "delivery_status" })
        ], cancellationToken: ct);
        await SearchIndexStates.Indexes.CreateOneAsync(new CreateIndexModel<SearchIndexState>(Builders<SearchIndexState>.IndexKeys.Ascending(x => x.LockedUntil), new CreateIndexOptions { Name = "search_state_lease" }), cancellationToken: ct);
    }

    async Task ReplaceLegacyNonUniqueFollowIndexAsync(CancellationToken ct)
    {
        var keys = new BsonDocument { { nameof(Follow.FollowerId), 1 }, { nameof(Follow.FolloweeId), 1 } };
        using var cursor = await Follows.Indexes.ListAsync(ct);
        foreach (var index in await cursor.ToListAsync(ct))
        {
            if (!index.TryGetValue("key", out var key) || !key.IsBsonDocument || !key.AsBsonDocument.Equals(keys)
                || (index.TryGetValue("unique", out var unique) && unique.ToBoolean())) continue;
            if (!index.TryGetValue("name", out var name) || name.AsString == "_id_")
                throw new InvalidOperationException("Cannot safely replace the legacy followee index.");
            await Follows.Indexes.DropOneAsync(name.AsString, ct);
        }
    }

    public Task<T> TransactionAsync<T>(Func<IClientSessionHandle, Task<T>> work, CancellationToken ct = default) =>
        RunTransaction(work, ct);

    public async Task TransactionAsync(Func<IClientSessionHandle, Task> work, CancellationToken ct = default) =>
        await RunTransaction(async session => { await work(session); return true; }, ct);

    async Task<T> RunTransaction<T>(Func<IClientSessionHandle, Task<T>> work, CancellationToken ct)
    {
        using var session = await client.StartSessionAsync(cancellationToken: ct);
        // Single mode runs the same write path without transactions (best effort, not atomic); replica mode requires them.
        if (mode == MongoMode.Single)
            return await work(session);
        return await session.WithTransactionAsync((s, _) => work(s), cancellationToken: ct);
    }
}

public sealed class User
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Email { get; set; }
    public required string Name { get; set; }
    public string PasswordHash { get; set; } = "";
    public DateTime? EmailVerifiedAt { get; set; }
    public Guid? AvatarAssetId { get; set; }
    public UserPreferences Preferences { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDemo { get; set; }
    [BsonIgnoreIfNull]
    public string? DemoKey { get; set; }
    public DateTime? NextDigestAtUtc { get; set; }
}

public sealed class UserPreferences
{
    public string Theme { get; set; } = "system";
    public bool EmailOnComment { get; set; } = true;
    public bool EmailOnLike { get; set; }
    public bool WeeklyDigest { get; set; } = true;
    public bool DailyRecommendationDigest { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public int PostsPerPage { get; set; } = 10;
}

public sealed class Post
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AuthorId { get; set; }
    [BsonIgnore] public User Author { get; set; } = null!;
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public required string Body { get; set; }
    public string Excerpt { get; set; } = "";
    public Guid? CoverAssetId { get; set; }
    [BsonIgnore] public Asset? Cover { get; set; }
    public int LikeCount { get; set; }
    public int CommentCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ArchivedAt { get; set; }
    [BsonIgnoreIfNull]
    public string? DemoKey { get; set; }
    public long IndexVersion { get; set; } = 1;
    public long SocialRevision { get; set; }
}

public sealed class Comment
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PostId { get; set; }
    [BsonIgnore] public Post Post { get; set; } = null!;
    public Guid UserId { get; set; }
    [BsonIgnore] public User User { get; set; } = null!;
    public required string Body { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Like
{
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
    [BsonId] public string Id { get => $"{PostId}:{UserId}"; set { var p = value.Split(':'); PostId = Guid.Parse(p[0]); UserId = Guid.Parse(p[1]); } }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum AssetStatus { Pending, Ready }
public sealed class Asset
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OwnerId { get; set; }
    public required string Key { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    [BsonRepresentation(BsonType.String)] public AssetStatus Status { get; set; } = AssetStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class PostView
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PostId { get; set; }
    public required string ViewerHash { get; set; }
    public string? Referrer { get; set; }
    public DateTime At { get; set; }
}

public sealed class PostDailyStat
{
    public Guid PostId { get; set; }
    [BsonRepresentation(BsonType.String)] public DateOnly Date { get; set; }
    [BsonId] public string Id { get => $"{PostId}:{Date:yyyy-MM-dd}"; set { var p = value.Split(':'); PostId = Guid.Parse(p[0]); Date = DateOnly.Parse(p[1]); } }
    public int Views { get; set; }
    public int UniqueViewers { get; set; }
    public int Likes { get; set; }
    public int Comments { get; set; }
}

public enum JobStatus { Pending, Running, Done, Failed }
public sealed class Job
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Type { get; set; }
    public required string Payload { get; set; }
    [BsonRepresentation(BsonType.String)] public JobStatus Status { get; set; } = JobStatus.Pending;
    public int Attempts { get; set; }
    public DateTime RunAt { get; set; } = DateTime.UtcNow;
    public DateTime? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class CronLease
{
    [BsonId] public required string Name { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime? LockedUntil { get; set; }
}
