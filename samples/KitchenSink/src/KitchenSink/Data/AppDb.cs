using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KitchenSink.Data;

public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<LiveVote> LiveVotes => Set<LiveVote>();
    public DbSet<LiveReaction> LiveReactions => Set<LiveReaction>();
    public DbSet<LiveTick> LiveTicks => Set<LiveTick>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite gives DateTime back as Unspecified; everything we store is UTC.
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Name).HasMaxLength(255);
            e.Property(u => u.Email).HasMaxLength(255);
        });

        b.Entity<Organization>(e =>
        {
            e.ToTable("organizations");
            e.Property(o => o.Name).HasMaxLength(255);
        });

        b.Entity<Contact>(e =>
        {
            e.ToTable("contacts");
            e.Property(c => c.FirstName).HasMaxLength(255);
            e.Property(c => c.LastName).HasMaxLength(255);
            e.Property(c => c.Email).HasMaxLength(255);
            e.Property(c => c.Phone).HasMaxLength(255);
            e.HasIndex(c => new { c.FirstName, c.LastName, c.Id });
            e.HasOne(c => c.Organization).WithMany(o => o.Contacts).HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Note>(e =>
        {
            e.ToTable("notes");
            e.HasIndex(n => new { n.CreatedAt, n.Id });
            e.HasIndex(n => new { n.ContactId, n.CreatedAt });
            e.HasOne(n => n.Contact).WithMany(c => c.Notes).HasForeignKey(n => n.ContactId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(n => n.User).WithMany(u => u.Notes).HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<LiveVote>(e =>
        {
            e.ToTable("live_votes");
            e.HasIndex(v => v.Visitor).IsUnique();
        });

        b.Entity<LiveReaction>(e =>
        {
            e.ToTable("live_reactions");
            e.HasIndex(r => r.Reaction);
        });

        b.Entity<LiveTick>(e => e.ToTable("live_ticks"));
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
