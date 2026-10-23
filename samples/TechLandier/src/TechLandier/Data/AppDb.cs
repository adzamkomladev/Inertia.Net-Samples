
using Microsoft.EntityFrameworkCore;
using TechLandier.Features;

namespace TechLandier.Data;

public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
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
    public DbSet<SeedCheckpoint> SeedCheckpoints => Set<SeedCheckpoint>();

    /// A one-row source: lets several independent counts run as scalar subqueries in a single round trip.
    public IQueryable<int> OneRow => Database.SqlQuery<int>($"SELECT 1 AS \"Value\"");

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<StoreSettings>();
        b.HasPostgresExtension("pg_trgm");

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
            e.HasIndex(x => new { x.Status, x.PublishedAt, x.Id });
            e.HasIndex(x => new { x.CategoryId, x.Status });
            // Full-text search (GIN) plus trigram matching on the name (typos and substrings).
            e.HasGeneratedTsVectorColumn(x => x.SearchVector, "english", x => new { x.Name, x.Brand, x.Keywords, x.Summary })
                .HasIndex(x => x.SearchVector).HasMethod("gin");
            e.HasIndex(x => x.Name).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.Property(x => x.Price).HasPrecision(12, 2);
            e.Property(x => x.CompareAtPrice).HasPrecision(12, 2);
            e.Property(x => x.RatingAvg).HasPrecision(3, 2);
            e.Property(x => x.SpecsJson).HasColumnType("jsonb");
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Brand).HasMaxLength(80);
            e.Property(x => x.Keywords).HasMaxLength(300);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);
            e.HasOne(x => x.ImageAsset).WithMany().HasForeignKey(x => x.ImageAssetId).OnDelete(DeleteBehavior.SetNull);
            e.ToTable(t => t.HasCheckConstraint("CK_Product_DiscountPercent", "\"DiscountPercent\" BETWEEN 0 AND 70"));
        });

        b.Entity<ProductImage>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.SortOrder });
            e.HasIndex(x => x.AssetId);
            e.Property(x => x.Url).HasMaxLength(1000);
            e.HasOne<Product>().WithMany(x => x.Images).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StockMovement>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.At });
            e.HasIndex(x => new { x.ProductId, x.Reason });
            e.Property(x => x.Reason).HasMaxLength(16);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Order>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.DemoKey).IsUnique();
            // Idempotency: a retried checkout with the same attempt id resolves to the same order.
            e.HasIndex(x => new { x.UserId, x.CheckoutAttemptId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CreatedAt, x.Id });
            e.HasIndex(x => new { x.CreatedAt, x.Id });
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
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
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
            e.HasOne(x => x.Order).WithMany(o => o.Items).HasForeignKey(x => x.OrderId);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<OrderEvent>(e =>
        {
            e.HasIndex(x => new { x.OrderId, x.At, x.Id });
            e.Property(x => x.Status).HasMaxLength(16);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId);
        });

        b.Entity<Payment>(e =>
        {
            e.HasIndex(x => x.OrderId);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Method).HasMaxLength(24);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.ProviderTxRef).HasMaxLength(64);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId);
        });

        b.Entity<PayPlan>(e =>
        {
            e.HasIndex(x => x.OrderId).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Status });
            e.Property(x => x.AmountPerInstallment).HasPrecision(12, 2);
            e.Property(x => x.FeeTotal).HasPrecision(12, 2);
            e.Property(x => x.Status).HasMaxLength(16);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<Installment>(e =>
        {
            e.HasIndex(x => new { x.PlanId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.Status, x.DueDate });
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.PaymentRef).HasMaxLength(64);
            e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId);
        });

        b.Entity<Warranty>(e =>
        {
            e.HasIndex(x => x.OrderItemId).IsUnique();
            e.HasIndex(x => new { x.UserId, x.EndOn });
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.SerialNumber).HasMaxLength(64);
            e.HasOne(x => x.OrderItem).WithOne().HasForeignKey<Warranty>(x => x.OrderItemId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<WarrantyClaim>(e =>
        {
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.Status).HasMaxLength(24);
            e.HasOne(x => x.Warranty).WithMany().HasForeignKey(x => x.WarrantyId);
        });

        b.Entity<DeviceInsurance>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Status });
            e.Property(x => x.Premium).HasPrecision(12, 2);
            e.Property(x => x.CoverageAmount).HasPrecision(12, 2);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.DeviceName).HasMaxLength(120);
            e.Property(x => x.DeviceSerial).HasMaxLength(64);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<InsuranceClaim>(e =>
        {
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.Status).HasMaxLength(24);
            e.Property(x => x.IncidentType).HasMaxLength(24);
            e.Property(x => x.PayoutAmount).HasPrecision(12, 2);
            e.HasOne(x => x.Policy).WithMany().HasForeignKey(x => x.DeviceInsuranceId);
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
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Review>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.UserId }).IsUnique();
            e.HasIndex(x => new { x.ProductId, x.Status, x.CreatedAt, x.Id });
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.OrderItem).WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.SetNull);
            e.ToTable(t => t.HasCheckConstraint("CK_Review_Rating", "\"Rating\" BETWEEN 1 AND 5"));
        });

        b.Entity<Ticket>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.Status, x.DueAt });
            e.HasIndex(x => new { x.UserId, x.UpdatedAt, x.Id });
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
            e.HasOne(x => x.Ticket).WithMany().HasForeignKey(x => x.TicketId);
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
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.ReceivedBy).HasMaxLength(80);
            e.HasOne(x => x.Order).WithOne().HasForeignKey<Delivery>(x => x.OrderId);
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
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
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
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        });

        b.Entity<Job>(e =>
        {
            e.HasIndex(x => new { x.Status, x.RunAt });
            e.Property(x => x.Status).HasConversion<string>();
        });

        b.Entity<CronLease>().HasKey(x => x.Name);

        b.Entity<SeedCheckpoint>().HasKey(x => x.Name);

        // Ids are UUIDv7 values assigned in app code (entity initializers), never by EF or the database.
        foreach (var key in b.Model.GetEntityTypes().Select(t => t.FindPrimaryKey()).Where(k => k is { Properties: [{ ClrType: var type }] } && type == typeof(Guid)))
            key!.Properties[0].ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
    }
}
