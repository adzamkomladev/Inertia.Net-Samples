using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;
using CarParker.Features.Alerts;
using CarParker.Features.Analytics;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
using CarParker.Features.Insurance;
using CarParker.Features.Lots;

namespace CarParker.Data;

public sealed class AppDb
{
    // Ids are UUIDv7 values assigned in app code; Standard (subtype 4) is the portable Guid wire format.
    static AppDb()
    {
        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        BsonSerializer.RegisterSerializer(new NullableSerializer<Guid>(new GuidSerializer(GuidRepresentation.Standard)));
        ConventionRegistry.Register("ignore-extra", new ConventionPack { new IgnoreExtraElementsConvention(true) }, _ => true);
    }

    readonly IMongoClient client;
    readonly ILogger<AppDb> logger;
    public MongoMode Mode { get; }

    public AppDb(IMongoClient client, IConfiguration configuration, ILogger<AppDb> logger)
    {
        this.client = client;
        this.logger = logger;
        Mode = MongoModeConfig.Parse(configuration["MONGO_MODE"]);
        var url = new MongoUrl(configuration["DATABASE_URL"] ?? configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("DATABASE_URL must be configured."));
        Database = client.GetDatabase(configuration["DATABASE_NAME"] ?? url.DatabaseName ?? "carparker");
        Users = Database.GetCollection<User>("users");
        Assets = Database.GetCollection<Asset>("assets");
        Jobs = Database.GetCollection<Job>("jobs");
        CronLeases = Database.GetCollection<CronLease>("cron_leases");
        Lots = Database.GetCollection<Lot>("lots");
        Spots = Database.GetCollection<Spot>("spots");
        Reservations = Database.GetCollection<Reservation>("reservations");
        Occurrences = Database.GetCollection<Occurrence>("occurrences");
        ParkingSessions = Database.GetCollection<ParkingSession>("sessions");
        Payments = Database.GetCollection<Payment>("payments");
        Refunds = Database.GetCollection<Refund>("refunds");
        SpotCoverPolicies = Database.GetCollection<SpotCoverPolicy>("policies");
        PolicyClaims = Database.GetCollection<PolicyClaim>("policy_claims");
        SecurityIncidents = Database.GetCollection<SecurityIncident>("incidents");
        Alerts = Database.GetCollection<Alert>("alerts");
        LotDailyStats = Database.GetCollection<LotDailyStat>("lot_daily_stats");
        SeedCheckpoints = Database.GetCollection<SeedCheckpoint>("seed_checkpoints");
    }

    public IMongoDatabase Database { get; }
    public IMongoCollection<User> Users { get; }
    public IMongoCollection<Asset> Assets { get; }
    public IMongoCollection<Job> Jobs { get; }
    public IMongoCollection<CronLease> CronLeases { get; }
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
    public IMongoCollection<SeedCheckpoint> SeedCheckpoints { get; }

    static CreateIndexModel<T> Index<T>(Func<IndexKeysDefinitionBuilder<T>, IndexKeysDefinition<T>> keys, string name, bool unique = false, bool sparse = false) =>
        new(keys(Builders<T>.IndexKeys), new CreateIndexOptions { Name = name, Unique = unique, Sparse = sparse });

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var hello = await Database.RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1), cancellationToken: ct);
        var transactional = hello.Contains("setName") || hello.GetValue("msg", "").AsString == "isdbgrid";
        if (Mode == MongoMode.Replica && !transactional)
            throw new InvalidOperationException("MONGO_MODE=replica requires a replica set or sharded cluster. This server is standalone; set MONGO_MODE=single to run without multi-document transactions, or configure a replica set.");
        if (Mode == MongoMode.Single)
            logger.LogWarning("MONGO_MODE=single: multi-document transactions are disabled; writes across collections are best effort and cannot roll back together.");
        else
            logger.LogInformation("MONGO_MODE=replica: MongoDB topology supports multi-document transactions.");
        await Task.WhenAll(
            Users.Indexes.CreateManyAsync([
                Index<User>(k => k.Ascending(x => x.Email), "email_unique", unique: true),
                Index<User>(k => k.Ascending(x => x.DemoKey), "demo_key_unique", unique: true, sparse: true),
                Index<User>(k => k.Ascending(x => x.Role).Ascending(x => x.CreatedAt), "role_created"),
            ], ct),
            Assets.Indexes.CreateManyAsync([
                Index<Asset>(k => k.Ascending(x => x.Key), "asset_key_unique", unique: true),
                Index<Asset>(k => k.Ascending(x => x.Status).Ascending(x => x.CreatedAt), "asset_cleanup"),
            ], ct),
            Lots.Indexes.CreateManyAsync([
                Index<Lot>(k => k.Ascending(x => x.DemoKey), "demo_key_unique", unique: true, sparse: true),
                Index<Lot>(k => k.Ascending(x => x.IsActive).Ascending(x => x.Name), "active_name"),
                Index<Lot>(k => k.Text(x => x.Name).Text(x => x.Address).Text(x => x.Area), "lot_text"),
            ], ct),
            Spots.Indexes.CreateManyAsync([
                Index<Spot>(k => k.Ascending(x => x.LotId).Ascending(x => x.Label), "lot_label_unique", unique: true),
                Index<Spot>(k => k.Ascending(x => x.LotId).Ascending(x => x.State).Ascending(x => x.Level), "lot_state_level"),
                Index<Spot>(k => k.Ascending(x => x.Type).Ascending(x => x.LotId), "type_lot"),
            ], ct),
            Reservations.Indexes.CreateManyAsync([
                Index<Reservation>(k => k.Ascending(x => x.UserId).Descending(x => x.CreatedAt).Descending(x => x.Id), "user_created"),
                Index<Reservation>(k => k.Ascending(x => x.SpotId).Ascending(x => x.Status), "spot_status"),
                Index<Reservation>(k => k.Ascending(x => x.LotId).Descending(x => x.CreatedAt), "lot_created"),
                Index<Reservation>(k => k.Descending(x => x.CreatedAt), "created"),
                new CreateIndexModel<Reservation>(Builders<Reservation>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.AttemptId),
                    new CreateIndexOptions<Reservation>
                    {
                        Unique = true,
                        Name = "user_attempt_unique_v2",
                        PartialFilterExpression = Builders<Reservation>.Filter.Type(x => x.AttemptId, BsonType.String),
                    }),
            ], ct),
            Occurrences.Indexes.CreateManyAsync([
                Index<Occurrence>(k => k.Ascending(x => x.ReservationId).Ascending(x => x.Index), "reservation_index"),
                Index<Occurrence>(k => k.Ascending(x => x.Status).Ascending(x => x.StartAtUtc), "status_start"),
            ], ct),
            ParkingSessions.Indexes.CreateManyAsync([
                Index<ParkingSession>(k => k.Ascending(x => x.Status).Descending(x => x.EnteredAtUtc), "status_entered"),
                Index<ParkingSession>(k => k.Ascending(x => x.SpotId).Ascending(x => x.Status), "spot_status"),
                Index<ParkingSession>(k => k.Ascending(x => x.OccurrenceId), "occurrence"),
                Index<ParkingSession>(k => k.Ascending(x => x.LotId).Ascending(x => x.Status), "lot_status"),
            ], ct),
            Payments.Indexes.CreateManyAsync([
                Index<Payment>(k => k.Ascending(x => x.ReservationId), "reservation"),
                Index<Payment>(k => k.Ascending(x => x.UserId).Descending(x => x.CreatedAt), "user_created"),
                Index<Payment>(k => k.Ascending(x => x.Status).Descending(x => x.PaidAt), "status_paid"),
            ], ct),
            Refunds.Indexes.CreateManyAsync([
                Index<Refund>(k => k.Ascending(x => x.Status).Ascending(x => x.CreatedAt), "status_created"),
                Index<Refund>(k => k.Ascending(x => x.ReservationId), "reservation"),
            ], ct),
            SpotCoverPolicies.Indexes.CreateManyAsync([
                Index<SpotCoverPolicy>(k => k.Ascending(x => x.ReservationId), "reservation_unique", unique: true),
                Index<SpotCoverPolicy>(k => k.Ascending(x => x.UserId).Descending(x => x.CreatedAt), "user_created"),
                Index<SpotCoverPolicy>(k => k.Ascending(x => x.Status), "status"),
            ], ct),
            PolicyClaims.Indexes.CreateManyAsync([
                Index<PolicyClaim>(k => k.Ascending(x => x.PolicyId), "policy"),
                Index<PolicyClaim>(k => k.Ascending(x => x.UserId).Descending(x => x.CreatedAt), "user_created"),
                Index<PolicyClaim>(k => k.Ascending(x => x.Status).Descending(x => x.CreatedAt), "status_created"),
            ], ct),
            SecurityIncidents.Indexes.CreateManyAsync([
                Index<SecurityIncident>(k => k.Ascending(x => x.Status).Descending(x => x.CreatedAt), "status_created"),
                Index<SecurityIncident>(k => k.Ascending(x => x.LotId).Descending(x => x.CreatedAt), "lot_created"),
            ], ct),
            Alerts.Indexes.CreateManyAsync([
                Index<Alert>(k => k.Ascending(x => x.UserId).Descending(x => x.At).Descending(x => x.Id), "user_recent"),
                Index<Alert>(k => k.Ascending(x => x.UserId).Ascending(x => x.ReadAt), "user_unread"),
                new CreateIndexModel<Alert>(Builders<Alert>.IndexKeys.Ascending(a => a.UserId).Ascending(a => a.SourceKey),
                    new CreateIndexOptions<Alert> { Name = "user_source", Unique = true, PartialFilterExpression = Builders<Alert>.Filter.Type(a => a.SourceKey, BsonType.String) }),
            ], ct),
            LotDailyStats.Indexes.CreateOneAsync(Index<LotDailyStat>(k => k.Ascending(x => x.LotId).Ascending(x => x.Date), "lot_day_unique", unique: true), cancellationToken: ct),
            Jobs.Indexes.CreateManyAsync([
                Index<Job>(k => k.Ascending(x => x.Status).Ascending(x => x.RunAt).Ascending(x => x.Id), "jobs_claim"),
                Index<Job>(k => k.Ascending(x => x.Status).Ascending(x => x.LockedUntil), "jobs_lease"),
            ], ct));
    }

    /// <summary>Replica mode uses a transaction; single mode runs the same session without a transaction.</summary>
    public async Task<T> TransactionAsync<T>(Func<IClientSessionHandle, Task<T>> work, CancellationToken ct = default)
    {
        using var session = await client.StartSessionAsync(cancellationToken: ct);
        // shortcut: single mode has no cross-document rollback; use replica mode when atomicity is required.
        if (Mode == MongoMode.Single) return await work(session);
        return await session.WithTransactionAsync(async (s, _) => await work(s), cancellationToken: ct);
    }

    public async Task TransactionAsync(Func<IClientSessionHandle, Task> work, CancellationToken ct = default) =>
        await TransactionAsync(async session => { await work(session); return true; }, ct);
}
