using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using SvelteMongoBlog.Features.Alerts;
using SvelteMongoBlog.Features.Analytics;
using SvelteMongoBlog.Features.Bookings;
using SvelteMongoBlog.Features.Guard;
using SvelteMongoBlog.Features.Incidents;
using SvelteMongoBlog.Features.Insurance;
using SvelteMongoBlog.Features.Lots;

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
        Assets = database.GetCollection<Asset>("assets");
        Jobs = database.GetCollection<Job>("jobs");
        CronLeases = database.GetCollection<CronLease>("cron_leases");
        Lots = database.GetCollection<Lot>("lots");
        Spots = database.GetCollection<Spot>("spots");
        Reservations = database.GetCollection<Reservation>("reservations");
        Occurrences = database.GetCollection<Occurrence>("occurrences");
        ParkingSessions = database.GetCollection<ParkingSession>("sessions");
        Payments = database.GetCollection<Payment>("payments");
        Refunds = database.GetCollection<Refund>("refunds");
        SpotCoverPolicies = database.GetCollection<SpotCoverPolicy>("policies");
        PolicyClaims = database.GetCollection<PolicyClaim>("policy_claims");
        SecurityIncidents = database.GetCollection<SecurityIncident>("incidents");
        Alerts = database.GetCollection<Alert>("alerts");
        LotDailyStats = database.GetCollection<LotDailyStat>("lot_daily_stats");
        SearchIndexStates = database.GetCollection<SearchIndexState>("search_index_states");
        SeedCheckpoints = database.GetCollection<SeedCheckpoint>("seed_checkpoints");
    }

    public IMongoCollection<User> Users { get; }
    public IMongoCollection<Asset> Assets { get; }
    public IMongoCollection<Job> Jobs { get; }
    public IMongoCollection<CronLease> CronLeases { get; }
    public IMongoDatabase Database => database;
    public IMongoCollection<Lot> Lots { get; }
    public IMongoCollection<Spot> Spots { get; }
    public IMongoCollection<Reservation> Reservations { get; }
    public IMongoCollection<Occurrence> Occurrences { get; }
    public IMongoCollection<ParkingSession> ParkingSessions { get; }
    public IMongoCollection<Payment> Payments { get; }
    public IMongoCollection<Refund> Refunds { get; }
    public IMongoCollection<SpotCoverPolicy> SpotCoverPolicies { get; }
    public IMongoCollection<PolicyClaim> PolicyClaims { get; }
    public IMongoCollection<SecurityIncident> SecurityIncidents { get; }
    public IMongoCollection<Alert> Alerts { get; }
    public IMongoCollection<LotDailyStat> LotDailyStats { get; }
    public IMongoCollection<SearchIndexState> SearchIndexStates { get; }
    public IMongoCollection<SeedCheckpoint> SeedCheckpoints { get; }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
        await Users.Indexes.CreateManyAsync([
            new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(x => x.Email), new CreateIndexOptions { Unique = true, Name = "email_unique" }),
            new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(x => x.DemoKey), new CreateIndexOptions { Unique = true, Sparse = true, Name = "demo_key_unique" }),
            new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(x => x.Role).Ascending(x => x.CreatedAt), new CreateIndexOptions { Name = "role_created" }),
        ], cancellationToken: ct);
        await Assets.Indexes.CreateManyAsync([
            new CreateIndexModel<Asset>(Builders<Asset>.IndexKeys.Ascending(x => x.Key), new CreateIndexOptions { Unique = true, Name = "asset_key_unique" }),
            new CreateIndexModel<Asset>(Builders<Asset>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.CreatedAt), new CreateIndexOptions { Name = "asset_cleanup" }),
        ], cancellationToken: ct);
        await Lots.Indexes.CreateManyAsync([
            new CreateIndexModel<Lot>(Builders<Lot>.IndexKeys.Ascending(x => x.DemoKey), new CreateIndexOptions { Unique = true, Sparse = true, Name = "demo_key_unique" }),
            new CreateIndexModel<Lot>(Builders<Lot>.IndexKeys.Ascending(x => x.IsActive).Descending(x => x.CreatedAt).Descending(x => x.Id), new CreateIndexOptions { Name = "active_created" }),
            new CreateIndexModel<Lot>(Builders<Lot>.IndexKeys.Text(x => x.Name).Text(x => x.Address).Text(x => x.Area), new CreateIndexOptions { Name = "lot_text" }),
        ], cancellationToken: ct);
        await Spots.Indexes.CreateManyAsync([
            new CreateIndexModel<Spot>(Builders<Spot>.IndexKeys.Ascending(x => x.LotId).Ascending(x => x.Label), new CreateIndexOptions { Unique = true, Name = "lot_label_unique" }),
            new CreateIndexModel<Spot>(Builders<Spot>.IndexKeys.Ascending(x => x.LotId).Ascending(x => x.State).Ascending(x => x.Level), new CreateIndexOptions { Name = "lot_state_level" }),
            new CreateIndexModel<Spot>(Builders<Spot>.IndexKeys.Ascending(x => x.State), new CreateIndexOptions { Name = "state" }),
        ], cancellationToken: ct);
        await Reservations.Indexes.CreateManyAsync([
            new CreateIndexModel<Reservation>(Builders<Reservation>.IndexKeys.Ascending(x => x.UserId).Descending(x => x.CreatedAt).Descending(x => x.Id), new CreateIndexOptions { Name = "user_created" }),
            new CreateIndexModel<Reservation>(Builders<Reservation>.IndexKeys.Ascending(x => x.SpotId).Ascending(x => x.Status), new CreateIndexOptions { Name = "spot_status" }),
            new CreateIndexModel<Reservation>(Builders<Reservation>.IndexKeys.Ascending(x => x.LotId).Descending(x => x.CreatedAt), new CreateIndexOptions { Name = "lot_created" }),
            new CreateIndexModel<Reservation>(Builders<Reservation>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.AttemptId), new CreateIndexOptions { Unique = true, Sparse = true, Name = "user_attempt_unique" }),
        ], cancellationToken: ct);
        await Occurrences.Indexes.CreateManyAsync([
            new CreateIndexModel<Occurrence>(Builders<Occurrence>.IndexKeys.Ascending(x => x.ReservationId).Ascending(x => x.Index), new CreateIndexOptions { Name = "reservation_index" }),
            new CreateIndexModel<Occurrence>(Builders<Occurrence>.IndexKeys.Ascending(x => x.StartAtUtc).Ascending(x => x.Status), new CreateIndexOptions { Name = "start_status" }),
            new CreateIndexModel<Occurrence>(Builders<Occurrence>.IndexKeys.Ascending(x => x.EndAtUtc).Ascending(x => x.Status), new CreateIndexOptions { Name = "end_status" }),
        ], cancellationToken: ct);
        await ParkingSessions.Indexes.CreateManyAsync([
            new CreateIndexModel<ParkingSession>(Builders<ParkingSession>.IndexKeys.Ascending(x => x.Status).Descending(x => x.EnteredAtUtc), new CreateIndexOptions { Name = "status_entered" }),
            new CreateIndexModel<ParkingSession>(Builders<ParkingSession>.IndexKeys.Ascending(x => x.SpotId).Ascending(x => x.Status), new CreateIndexOptions { Name = "spot_status" }),
            new CreateIndexModel<ParkingSession>(Builders<ParkingSession>.IndexKeys.Ascending(x => x.OccurrenceId), new CreateIndexOptions { Name = "occurrence" }),
        ], cancellationToken: ct);
        await Payments.Indexes.CreateManyAsync([
            new CreateIndexModel<Payment>(Builders<Payment>.IndexKeys.Ascending(x => x.ReservationId), new CreateIndexOptions { Name = "reservation" }),
            new CreateIndexModel<Payment>(Builders<Payment>.IndexKeys.Ascending(x => x.Status).Descending(x => x.CreatedAt), new CreateIndexOptions { Name = "status_created" }),
        ], cancellationToken: ct);
        await Refunds.Indexes.CreateManyAsync([
            new CreateIndexModel<Refund>(Builders<Refund>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.CreatedAt), new CreateIndexOptions { Name = "status_created" }),
            new CreateIndexModel<Refund>(Builders<Refund>.IndexKeys.Ascending(x => x.ReservationId), new CreateIndexOptions { Name = "reservation" }),
        ], cancellationToken: ct);
        await SpotCoverPolicies.Indexes.CreateManyAsync([
            new CreateIndexModel<SpotCoverPolicy>(Builders<SpotCoverPolicy>.IndexKeys.Ascending(x => x.ReservationId), new CreateIndexOptions { Unique = true, Name = "reservation_unique" }),
            new CreateIndexModel<SpotCoverPolicy>(Builders<SpotCoverPolicy>.IndexKeys.Ascending(x => x.UserId).Descending(x => x.CreatedAt), new CreateIndexOptions { Name = "user_created" }),
            new CreateIndexModel<SpotCoverPolicy>(Builders<SpotCoverPolicy>.IndexKeys.Ascending(x => x.EndedAt).Ascending(x => x.Status), new CreateIndexOptions { Name = "expiry_check" }),
        ], cancellationToken: ct);
        await PolicyClaims.Indexes.CreateManyAsync([
            new CreateIndexModel<PolicyClaim>(Builders<PolicyClaim>.IndexKeys.Ascending(x => x.PolicyId), new CreateIndexOptions { Name = "policy" }),
            new CreateIndexModel<PolicyClaim>(Builders<PolicyClaim>.IndexKeys.Ascending(x => x.Status).Descending(x => x.CreatedAt), new CreateIndexOptions { Name = "status_created" }),
        ], cancellationToken: ct);
        await SecurityIncidents.Indexes.CreateManyAsync([
            new CreateIndexModel<SecurityIncident>(Builders<SecurityIncident>.IndexKeys.Ascending(x => x.Status).Descending(x => x.CreatedAt), new CreateIndexOptions { Name = "status_created" }),
            new CreateIndexModel<SecurityIncident>(Builders<SecurityIncident>.IndexKeys.Ascending(x => x.LotId).Descending(x => x.CreatedAt), new CreateIndexOptions { Name = "lot_created" }),
        ], cancellationToken: ct);
        await Alerts.Indexes.CreateManyAsync([
            new CreateIndexModel<Alert>(Builders<Alert>.IndexKeys.Ascending(x => x.UserId).Descending(x => x.At).Descending(x => x.Id), new CreateIndexOptions { Name = "user_recent" }),
            new CreateIndexModel<Alert>(Builders<Alert>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.ReadAt), new CreateIndexOptions { Name = "user_unread" }),
        ], cancellationToken: ct);
        await LotDailyStats.Indexes.CreateOneAsync(new CreateIndexModel<LotDailyStat>(Builders<LotDailyStat>.IndexKeys.Ascending(x => x.LotId).Ascending(x => x.Date), new CreateIndexOptions { Unique = true, Name = "lot_day_unique" }), cancellationToken: ct);
        await Jobs.Indexes.CreateManyAsync([
            new CreateIndexModel<Job>(Builders<Job>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.RunAt).Ascending(x => x.Id), new CreateIndexOptions { Name = "jobs_claim" }),
            new CreateIndexModel<Job>(Builders<Job>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.LockedUntil), new CreateIndexOptions { Name = "jobs_lease" }),
        ], cancellationToken: ct);
        await SearchIndexStates.Indexes.CreateOneAsync(new CreateIndexModel<SearchIndexState>(Builders<SearchIndexState>.IndexKeys.Ascending(x => x.LockedUntil), new CreateIndexOptions { Name = "search_state_lease" }), cancellationToken: ct);
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

public enum UserRole { Customer, Admin, Guard }

public sealed class User
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Email { get; set; }
    public required string Name { get; set; }
    public string PasswordHash { get; set; } = "";
    public string? Phone { get; set; }
    [BsonRepresentation(BsonType.String)] public UserRole Role { get; set; } = UserRole.Customer;
    public DateTime? EmailVerifiedAt { get; set; }
    public Guid? AvatarAssetId { get; set; }
    public UserPreferences Preferences { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDemo { get; set; }
    [BsonIgnoreIfNull]
    public string? DemoKey { get; set; }
}

public sealed class UserPreferences
{
    public string Theme { get; set; } = "system";
    public bool BookingReminders { get; set; } = true;
    public bool EmailOnCheckout { get; set; } = true;
    public bool EmailOnRefund { get; set; } = true;
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

/// <summary>Durable Meilisearch intent per lot; PostId==Guid.Empty row is the setup lane, like the old machinery.</summary>
public sealed class SearchIndexState
{
    [BsonId] public Guid LotId { get; set; }
    public long RequestedVersion { get; set; }
    public long AppliedVersion { get; set; }
    public long? TaskId { get; set; }
    public long? TaskVersion { get; set; }
    public string? LeaseToken { get; set; }
    public DateTime? LockedUntil { get; set; }
}

public sealed class SeedCheckpoint
{
    [BsonId] public required string Name { get; set; }
    public int Completed { get; set; }
    public int Seed { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
