using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using SvelteMongoBlog.Data;

namespace SvelteMongoBlog.Tests;

public sealed class IndexMigrationTests
{
    [Fact]
    public async Task Initialize_replaces_legacy_nonunique_follow_index_and_preserves_edges()
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(AppDb).TypeHandle); // registers the Guid serializer
        var databaseName = "svelte_index_migration_" + Guid.NewGuid().ToString("N");
        var client = new MongoClient(AppFactory.TestConnection);
        var database = client.GetDatabase(databaseName);
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var follows = database.GetCollection<Follow>("follows");
        try
        {
            await follows.InsertOneAsync(new Follow { FollowerId = a, FolloweeId = b });
            await follows.Indexes.CreateOneAsync(new CreateIndexModel<Follow>(
                Builders<Follow>.IndexKeys.Ascending(x => x.FollowerId).Ascending(x => x.FolloweeId),
                new CreateIndexOptions { Name = "followees_by_follower" }));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = new MongoUrlBuilder(AppFactory.TestConnection) { DatabaseName = databaseName }.ToMongoUrl().ToString()
            }).Build();

            await new AppDb(client, config).InitializeAsync();

            using var cursor = await follows.Indexes.ListAsync();
            var indexes = await cursor.ToListAsync();
            Assert.Contains(indexes, index => index["name"].AsString == "followees_by_follower"
                && index.TryGetValue("unique", out var unique) && unique.ToBoolean()
                && index["key"].AsBsonDocument.Equals(new BsonDocument { { nameof(Follow.FollowerId), 1 }, { nameof(Follow.FolloweeId), 1 } }));
            Assert.Equal(1, await follows.CountDocumentsAsync(x => x.FollowerId == a && x.FolloweeId == b));
        }
        finally
        {
            await client.DropDatabaseAsync(databaseName);
        }
    }
}
