using CarParker.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CarParker.Tests;

public class MongoModeTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Theory]
    [InlineData(null, MongoMode.Single)]
    [InlineData("", MongoMode.Single)]
    [InlineData(" single ", MongoMode.Single)]
    [InlineData("REPLICA", MongoMode.Replica)]
    [InlineData("replica-set", MongoMode.Replica)]
    [InlineData("replicaset", MongoMode.Replica)]
    public void Parses_mode(string? value, MongoMode expected) => Assert.Equal(expected, MongoModeConfig.Parse(value));

    [Fact]
    public void Invalid_mode_fails_clearly() =>
        Assert.Contains("MONGO_MODE", Assert.Throws<InvalidOperationException>(() => MongoModeConfig.Parse("typo")).Message);

    [Fact]
    public async Task Failed_work_rolls_back_only_in_replica_mode()
    {
        await app.QueryAsync(async db =>
        {
            var markers = db.Database.GetCollection<BsonDocument>("mode_test");
            await db.Database.CreateCollectionAsync("mode_test");
            var id = ObjectId.GenerateNewId();
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await db.TransactionAsync(async session =>
                {
                    Assert.Equal(db.Mode == MongoMode.Replica, session.IsInTransaction);
                    await markers.InsertOneAsync(session, new BsonDocument("_id", id));
                    throw new InvalidOperationException("intentional failure");
                }));
            Assert.Equal(db.Mode == MongoMode.Single ? 1L : 0L,
                await markers.CountDocumentsAsync(new BsonDocument("_id", id)));
            return true;
        });
    }

    [Fact]
    public async Task Replica_mode_checks_actual_server_topology()
    {
        await app.QueryAsync(async db =>
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = AppFactory.TestConnection,
                ["DATABASE_NAME"] = db.Database.DatabaseNamespace.DatabaseName,
                ["MONGO_MODE"] = "replica",
            }).Build();
            var replicaDb = new AppDb(db.Database.Client, config, NullLogger<AppDb>.Instance);
            if (MongoModeConfig.Parse(AppFactory.TestMode) == MongoMode.Single)
                Assert.Contains("standalone", (await Assert.ThrowsAsync<InvalidOperationException>(async () => await replicaDb.InitializeAsync())).Message);
            else
                await replicaDb.InitializeAsync();
            return true;
        });
    }
}
