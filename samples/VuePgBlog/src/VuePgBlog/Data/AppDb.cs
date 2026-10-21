
using Microsoft.EntityFrameworkCore;
using VuePgBlog.Features;


namespace VuePgBlog.Data;

public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PayPlan> PayPlans => Set<PayPlan>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<Warranty> Warranties => Set<Warranty>();
    public DbSet<WarrantyClaim> WarrantyClaims => Set<WarrantyClaim>();
    public DbSet<DeviceInsurance> DeviceInsurances => Set<DeviceInsurance>();
    public DbSet<InsuranceClaim> InsuranceClaims => Set<InsuranceClaim>();
    public DbSet<RepairTicket> RepairTickets => Set<RepairTicket>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();
    public DbSet<DeliveryZone> DeliveryZones => Set<DeliveryZone>();
    public DbSet<Rider> Riders => Set<Rider>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<SalesDailyStat> SalesDailyStats => Set<SalesDailyStat>();
    public DbSet<ProductView> ProductViews => Set<ProductView>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<CronLease> CronLeases => Set<CronLease>();
    public DbSet<SearchIndexState> SearchIndexStates => Set<SearchIndexState>();
    public DbSet<SeedCheckpoint> SeedCheckpoints => Set<SeedCheckpoint>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.DemoKey).IsUnique();
            e.HasIndex(x => new { x.Role, x.EmailVerifiedAt });
            e.OwnsOne(x => x.Preferences, p => p.ToJson());
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AvatarAssetId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Asset>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.Status).HasConversion<string>();
        });

        b.Entity<Category>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.SortOrder);
        });

        b.Entity<Product>(e =>
        {
            // Storefront reads only see active listings; admin passes IgnoreQueryFilters().
            e.HasQueryFilter(x => x.Status == ProductStatuses.Active);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.DemoKey).IsUnique();
            e.HasIndex(x => new { x.Status, x.CreatedAt, x.Id });
            e.HasIndex(x => new { x.CategoryId, x.Status });
            e.HasIndex(x => new { x.Stock, x.LowStockThreshold });
            e.HasIndex(x => x.Price);
            e.Property(x => x.Price).HasPrecision(12, 2);
            e.Property(x => x.CompareAtPrice).HasPrecision(12, 2);
            e.Property(x => x.RatingAvg).HasPrecision(3, 2);
            e.Property(x => x.SpecsJson).HasColumnType("jsonb");
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Brand).HasMaxLength(80);
            e.Property(x => x.Keywords).HasMaxLength(300);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ImageAsset).WithMany().HasForeignKey(x => x.ImageAssetId).OnDelete(DeleteBehavior.SetNull);
            e.ToTable(t => t.HasCheckConstraint("CK_Product_DiscountPercent", "\"DiscountPercent\" BETWEEN 0 AND 70"));
        });

        b.Entity<StockMovement>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.At });
            e.HasIndex(x => new { x.ProductId, x.Reason });
            e.Property(x => x.Reason).HasMaxLength(16);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Order>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.DemoKey).IsUnique();
            // Idempotency: a retried checkout with the same attempt id resolves to the same order.
            e.HasIndex(x => new { x.UserId, x.CheckoutAttemptId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CreatedAt, x.Id });
            e.HasIndex(x => x.Status);
            e.HasIndex(x => new { x.Status, x.PaidAt });
            e.Property(x => x.Subtotal).HasPrecision(12, 2);
            e.Property(x => x.DiscountTotal).HasPrecision(12, 2);
            e.Property(x => x.ShippingFee).HasPrecision(12, 2);
            e.Property(x => x.Total).HasPrecision(12, 2);
            e.Property(x => x.Number).HasMaxLength(24);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.PaymentMethod).HasMaxLength(24);
            e.Property(x => x.PaymentStatus).HasMaxLength(16);
            e.Property(x => x.AccessToken).HasMaxLength(24);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Zone).WithMany().HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.SetNull);
            e.OwnsOne(x => x.AddressSnapshot, a => a.ToJson());
        });

        b.Entity<OrderItem>(e =>
        {
            e.HasIndex(x => new { x.OrderId, x.Id });
            e.HasIndex(x => x.ProductId);
            e.Property(x => x.UnitPrice).HasPrecision(12, 2);
            e.Property(x => x.LineTotal).HasPrecision(12, 2);
            e.Property(x => x.Sku).HasMaxLength(64);
            // Bind the Order.Items collection to the same FK so no shadow OrderId1 column appears.
            e.HasOne(x => x.Order).WithMany(o => o.Items).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<OrderEvent>(e =>
        {
            e.HasIndex(x => new { x.OrderId, x.At, x.Id });
            e.Property(x => x.Status).HasMaxLength(16);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Payment>(e =>
        {
            e.HasIndex(x => x.OrderId);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Method).HasMaxLength(24);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.ProviderTxRef).HasMaxLength(64);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PayPlan>(e =>
        {
            e.HasIndex(x => x.OrderId).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Status });
            e.Property(x => x.AmountPerInstallment).HasPrecision(12, 2);
            e.Property(x => x.FeeTotal).HasPrecision(12, 2);
            e.Property(x => x.Status).HasMaxLength(16);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Installment>(e =>
        {
            e.HasIndex(x => new { x.PlanId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.Status, x.DueDate });
            e.HasIndex(x => x.DueDate);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.PaymentRef).HasMaxLength(64);
            e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Warranty>(e =>
        {
            e.HasIndex(x => x.OrderItemId).IsUnique();
            e.HasIndex(x => new { x.UserId, x.EndOn });
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.SerialNumber).HasMaxLength(64);
            e.HasOne(x => x.OrderItem).WithOne().HasForeignKey<Warranty>(x => x.OrderItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<WarrantyClaim>(e =>
        {
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.Status).HasMaxLength(24);
            e.HasOne(x => x.Warranty).WithMany().HasForeignKey(x => x.WarrantyId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DeviceInsurance>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Status });
            e.Property(x => x.Premium).HasPrecision(12, 2);
            e.Property(x => x.CoverageAmount).HasPrecision(12, 2);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.DeviceName).HasMaxLength(120);
            e.Property(x => x.DeviceSerial).HasMaxLength(64);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<InsuranceClaim>(e =>
        {
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.Status).HasMaxLength(24);
            e.Property(x => x.IncidentType).HasMaxLength(24);
            e.Property(x => x.PayoutAmount).HasPrecision(12, 2);
            e.HasOne(x => x.Policy).WithMany().HasForeignKey(x => x.DeviceInsuranceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RepairTicket>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.StatusHistoryJson).HasColumnType("jsonb");
            e.Property(x => x.QuotedCost).HasPrecision(12, 2);
            e.Property(x => x.DeviceName).HasMaxLength(120);
            e.Property(x => x.DeviceSerial).HasMaxLength(64);
            e.Property(x => x.TechnicianName).HasMaxLength(80);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Review>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.UserId }).IsUnique();
            e.HasIndex(x => new { x.ProductId, x.Status, x.CreatedAt, x.Id });
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.OrderItem).WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.SetNull);
            e.ToTable(t => t.HasCheckConstraint("CK_Review_Rating", "\"Rating\" BETWEEN 1 AND 5"));
        });

        b.Entity<Ticket>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.Status, x.DueAt });
            e.HasIndex(x => new { x.UserId, x.CreatedAt, x.Id });
            e.Property(x => x.Number).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(24);
            e.Property(x => x.Priority).HasMaxLength(16);
            e.Property(x => x.Category).HasMaxLength(24);
            e.Property(x => x.Subject).HasMaxLength(120);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<TicketMessage>(e =>
        {
            e.HasIndex(x => new { x.TicketId, x.At, x.Id });
            e.HasOne(x => x.Ticket).WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DeliveryZone>(e =>
        {
            e.HasIndex(x => x.SortOrder);
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Fee).HasPrecision(12, 2);
            e.Property(x => x.EtaText).HasMaxLength(64);
        });

        b.Entity<Rider>(e =>
        {
            e.HasIndex(x => new { x.IsActive, x.Name });
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Phone).HasMaxLength(32);
            e.Property(x => x.Vehicle).HasMaxLength(80);
            e.Property(x => x.Service).HasMaxLength(16);
        });

        b.Entity<Delivery>(e =>
        {
            e.HasIndex(x => x.OrderId).IsUnique();
            e.HasIndex(x => new { x.Status, x.AssignedAt });
            e.HasIndex(x => new { x.RiderId, x.Status });
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.FeeCollected).HasPrecision(12, 2);
            e.Property(x => x.CashCollected).HasPrecision(12, 2);
            e.Property(x => x.FailedReason).HasMaxLength(200);
            e.HasOne(x => x.Order).WithOne().HasForeignKey<Delivery>(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Zone).WithMany().HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Rider).WithMany().HasForeignKey(x => x.RiderId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Alert>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.CreatedAt, x.Id });
            e.HasIndex(x => new { x.UserId, x.ReadAt });
            e.Property(x => x.Kind).HasMaxLength(24);
            e.Property(x => x.Title).HasMaxLength(120);
            e.Property(x => x.Body).HasMaxLength(500);
            e.Property(x => x.Href).HasMaxLength(200);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SalesDailyStat>(e =>
        {
            e.HasKey(x => x.Date);
            e.Property(x => x.Revenue).HasPrecision(12, 2);
            e.Property(x => x.BnplShare).HasPrecision(12, 2);
        });

        b.Entity<ProductView>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.At });
            e.HasIndex(x => x.At);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Job>(e =>
        {
            e.HasIndex(x => new { x.Status, x.RunAt });
            e.Property(x => x.Status).HasConversion<string>();
        });

        b.Entity<CronLease>().HasKey(x => x.Name);

        b.Entity<SearchIndexState>(e =>
        {
            e.HasKey(x => x.ProductId);
            e.HasIndex(x => x.LockedUntil);
            e.HasIndex(x => x.ProductId).HasFilter("\"RequestedVersion\" > \"AppliedVersion\""); // pending reindex rows only
        });
        b.Entity<SeedCheckpoint>().HasKey(x => x.Name);

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
    /// Role claim ("customer" | "admin"); seeded admins only, public registration is always a customer.
    public string Role { get; set; } = "customer";
    public string PasswordHash { get; set; } = "";
    public DateTime? EmailVerifiedAt { get; set; }
    public Guid? AvatarAssetId { get; set; }
    public UserPreferences Preferences { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDemo { get; set; }
    public string? DemoKey { get; set; }
}

public sealed class UserPreferences
{
    public string Theme { get; set; } = "system"; // system | light | dark
    public bool EmailOrderUpdates { get; set; } = true;
    public bool EmailInstallmentReminders { get; set; } = true;
    public bool EmailSupportReplies { get; set; } = true;
    public bool EmailPromos { get; set; } = false;
    public string TimeZoneId { get; set; } = "UTC";
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

// Retained when a product is deleted so the external search document can be removed.
public sealed class SearchIndexState
{
    public Guid ProductId { get; set; }
    public long RequestedVersion { get; set; }
    public long AppliedVersion { get; set; }
    public long? TaskId { get; set; }
    public long? TaskVersion { get; set; }
    public string? LeaseToken { get; set; }
    public DateTime? LockedUntil { get; set; }
}

public sealed class SeedCheckpoint
{
    public required string Name { get; set; }
    public int Completed { get; set; }
    public int Seed { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
