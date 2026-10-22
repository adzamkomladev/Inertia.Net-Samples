using Microsoft.EntityFrameworkCore;

namespace Inkwell.Data;

public static class SocialModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<PostTopic>(e =>
        {
            e.HasKey(x => new { x.PostId, x.Topic });
            e.Property(x => x.Topic).HasMaxLength(32);
            e.HasIndex(x => new { x.Topic, x.PostId });
            e.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Follow>(e =>
        {
            e.HasKey(x => new { x.FollowerId, x.FolloweeId });
            e.HasIndex(x => new { x.FolloweeId, x.FollowerId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.FollowerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.FolloweeId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_Follow_NotSelf", "\"FollowerId\" <> \"FolloweeId\""));
        });
        b.Entity<Bookmark>(e =>
        {
            e.HasKey(x => new { x.UserId, x.PostId });
            e.HasIndex(x => new { x.UserId, x.CreatedAt, x.PostId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CommentLike>(e =>
        {
            e.HasKey(x => new { x.CommentId, x.UserId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Comment>().WithMany().HasForeignKey(x => x.CommentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Repost>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.PostId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CreatedAt, x.Id });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SeenPost>(e =>
        {
            e.HasKey(x => new { x.UserId, x.PostId });
            e.HasIndex(x => new { x.UserId, x.LastSeenAt });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<InterestProfile>(e =>
        {
            e.HasKey(x => x.UserId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Dirty, x.UpdatedAt });
        });
        b.Entity<FeedSnapshot>(e =>
        {
            e.HasIndex(x => x.ExpiresAt);
            e.HasIndex(x => new { x.UserId, x.Mode, x.CreatedAt });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<DigestDelivery>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.LocalDate }).IsUnique();
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SeedCheckpoint>().HasKey(x => x.Name);
    }
}
