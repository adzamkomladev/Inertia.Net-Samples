using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Inkwell.Data;

public sealed class AppDb(DbContextOptions<AppDb> options) : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<PostLike> PostLikes => Set<PostLike>();
    public DbSet<PostView> PostViews => Set<PostView>();
    public DbSet<Upload> Uploads => Set<Upload>();
    public DbSet<Job> Jobs => Set<Job>();
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

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite gives DateTime back as Unspecified; everything we store is UTC.
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        SocialModel.Configure(b);
        b.Entity<AppUser>().HasIndex(u => u.DemoKey).IsUnique();
        b.Entity<AppUser>().HasIndex(u => u.NextDigestAtUtc);

        b.Entity<AppUser>().OwnsOne(u => u.Preferences, p =>
        {
            p.Property(x => x.Theme).HasMaxLength(10).HasDefaultValue("system");
        });

        b.Entity<Post>(e =>
        {
            e.HasQueryFilter(p => p.ArchivedAt == null && p.IsPublished);
            e.HasIndex(p => p.DemoKey).IsUnique();
            e.HasIndex(p => new { p.CreatedAt, p.Id });
            e.HasIndex(p => new { p.PublishedAt, p.Id });
            e.Property(p => p.Title).HasMaxLength(200);
            e.HasIndex(p => new { p.AuthorId, p.CreatedAt });
            e.HasOne(p => p.Author).WithMany().HasForeignKey(p => p.AuthorId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Comment>(e =>
        {
            e.HasQueryFilter(c => c.Post.ArchivedAt == null && c.Post.IsPublished);
            e.HasIndex(c => new { c.AuthorId, c.CreatedAt, c.PostId });
            e.HasIndex(c => new { c.PostId, c.CreatedAt, c.Id });
            e.HasOne(c => c.Post).WithMany(p => p.Comments).HasForeignKey(c => c.PostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Author).WithMany().HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PostLike>(e =>
        {
            e.HasKey(l => new { l.PostId, l.UserId });
            e.HasIndex(l => new { l.UserId, l.CreatedAt, l.PostId });
            e.HasIndex(l => new { l.PostId, l.CreatedAt });
            e.HasOne<Post>().WithMany().HasForeignKey(l => l.PostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PostView>(e =>
        {
            e.HasKey(v => new { v.PostId, v.VisitorHash, v.Day });
            e.HasIndex(v => new { v.PostId, v.Day });
            e.HasIndex(v => v.Day);
            e.HasOne<Post>().WithMany().HasForeignKey(v => v.PostId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Upload>(e =>
        {
            e.HasIndex(u => u.Key).IsUnique();
            e.HasIndex(u => new { u.PostId, u.CreatedAt });
            e.HasOne<AppUser>().WithMany().HasForeignKey(u => u.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Post>().WithMany().HasForeignKey(u => u.PostId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Job>(e =>
        {
            e.Property(j => j.Type).HasMaxLength(100);
            e.HasIndex(j => new { j.Status, j.RunAt });
        });
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
