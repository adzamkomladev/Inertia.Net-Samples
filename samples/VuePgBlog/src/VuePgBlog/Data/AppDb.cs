
using Microsoft.EntityFrameworkCore;


namespace VuePgBlog.Data;

public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Like> Likes => Set<Like>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<PostView> PostViews => Set<PostView>();
    public DbSet<PostDailyStat> PostDailyStats => Set<PostDailyStat>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<CronLease> CronLeases => Set<CronLease>();
    public DbSet<PostTopic> PostTopics => Set<PostTopic>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();
    public DbSet<CommentLike> CommentLikes => Set<CommentLike>();
    public DbSet<Repost> Reposts => Set<Repost>();
    public DbSet<SeenPost> SeenPosts => Set<SeenPost>();
    public DbSet<InterestProfile> InterestProfiles => Set<InterestProfile>();
    public DbSet<FeedSnapshot> FeedSnapshots => Set<FeedSnapshot>();
    public DbSet<DigestDelivery> DigestDeliveries => Set<DigestDelivery>();
    public DbSet<SearchIndexState> SearchIndexStates => Set<SearchIndexState>();
    public DbSet<SeedCheckpoint> SeedCheckpoints => Set<SeedCheckpoint>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        SocialModel.Configure(b);
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.DemoKey).IsUnique();
            e.HasIndex(x => x.NextDigestAtUtc);
            e.OwnsOne(x => x.Preferences, p => p.ToJson());
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AvatarAssetId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Post>(e =>
        {
            e.HasQueryFilter(x => x.ArchivedAt == null);
            e.HasIndex(x => x.DemoKey).IsUnique();
            e.HasIndex(x => new { x.CreatedAt, x.Id });
            e.HasIndex(x => new { x.PublishedAt, x.Id });
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.AuthorId, x.CreatedAt });
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Cover).WithMany().HasForeignKey(x => x.CoverAssetId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Comment>(e =>
        {
            e.HasQueryFilter(x => x.Post.ArchivedAt == null);
            e.HasIndex(x => new { x.UserId, x.CreatedAt, x.PostId });
            e.HasIndex(x => new { x.PostId, x.CreatedAt, x.Id });
            e.HasOne(x => x.Post).WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Like>(e =>
        {
            e.HasKey(x => new { x.PostId, x.UserId });
            e.HasIndex(x => new { x.UserId, x.CreatedAt, x.PostId });
            e.HasIndex(x => new { x.PostId, x.CreatedAt });
            e.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Asset>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.Status).HasConversion<string>();
        });

        b.Entity<PostView>(e =>
        {
            e.HasIndex(x => new { x.PostId, x.At });
            e.HasIndex(x => x.At);
            e.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PostDailyStat>(e =>
        {
            e.HasKey(x => new { x.PostId, x.Date });
            e.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Job>(e =>
        {
            e.HasIndex(x => new { x.Status, x.RunAt });
            e.Property(x => x.Status).HasConversion<string>();
        });

        b.Entity<CronLease>().HasKey(x => x.Name);

        // Ids are UUIDv7 values assigned in app code (entity initializers), never by EF or the database.
        foreach (var key in b.Model.GetEntityTypes().Select(t => t.FindPrimaryKey()).Where(k => k is { Properties: [{ ClrType: var type }] } && type == typeof(Guid)))
            key!.Properties[0].ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
    }
}

public sealed class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Email { get; set; }
    public required string Name { get; set; }
    public string PasswordHash { get; set; } = "";
    public DateTime? EmailVerifiedAt { get; set; }
    public Guid? AvatarAssetId { get; set; }
    public UserPreferences Preferences { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDemo { get; set; }
    public string? DemoKey { get; set; }
    public DateTime? NextDigestAtUtc { get; set; }
}

public sealed class UserPreferences
{
    public string Theme { get; set; } = "system"; // system | light | dark
    public bool EmailOnComment { get; set; } = true;
    public bool EmailOnLike { get; set; } = false;
    public bool WeeklyDigest { get; set; } = true;
    public bool DailyRecommendationDigest { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public int PostsPerPage { get; set; } = 10;
}

public sealed class Post
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public required string Body { get; set; }
    public string Excerpt { get; set; } = "";
    public Guid? CoverAssetId { get; set; }
    public Asset? Cover { get; set; }
    public int LikeCount { get; set; }
    public int CommentCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ArchivedAt { get; set; }
    public string? DemoKey { get; set; }
    public long IndexVersion { get; set; } = 1;
}

public sealed class Comment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PostId { get; set; }
    public Post Post { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public required string Body { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Like
{
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum AssetStatus { Pending, Ready }

public sealed class Asset
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OwnerId { get; set; }
    public required string Key { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class PostView
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PostId { get; set; }
    public required string ViewerHash { get; set; }
    public string? Referrer { get; set; }
    public DateTime At { get; set; }
}

public sealed class PostDailyStat
{
    public Guid PostId { get; set; }
    public DateOnly Date { get; set; }
    public int Views { get; set; }
    public int UniqueViewers { get; set; }
    public int Likes { get; set; }
    public int Comments { get; set; }
}

public enum JobStatus { Pending, Running, Done, Failed }

public sealed class Job
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Type { get; set; }
    public required string Payload { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int Attempts { get; set; }
    public DateTime RunAt { get; set; } = DateTime.UtcNow;
    public DateTime? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class CronLease
{
    public required string Name { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime? LockedUntil { get; set; }
}
