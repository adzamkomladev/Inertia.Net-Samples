using System.Net.Sockets;
using MongoDB.Bson;
using MongoDB.Driver;
using CarParker.Data;

namespace CarParker.Infrastructure.Setup;

/// MongoDB mode is configured with MONGO_MODE (default: single).
/// single: the app runs against any server; multi-document writes execute without transactions (best effort).
/// replica: registration, comments, likes and cron leases use multi-document transactions, so the configured
/// server must be a replica set (or sharded cluster); a standalone target stops the app with a clear message.
public static class MongoTransactionSetup
{
    public static async Task VerifyMongoTransactionsAsync(this WebApplication app, CancellationToken ct = default)
    {
        var mode = MongoModeConfig.Parse(app.Configuration["MONGO_MODE"]); // fails fast on an invalid value
        var url = app.Configuration["DATABASE_URL"] ?? app.Configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(url)) return; // AddBlogData already stops with a clear configuration error.

        try
        {
            // Probe the configured server directly: replica-set URIs hide a standalone target behind server selection.
            var settings = MongoClientSettings.FromConnectionString(url);
            settings.DirectConnection = true;
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            settings.ConnectTimeout = TimeSpan.FromSeconds(5);
            using var client = new MongoClient(settings);
            var hello = await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1), cancellationToken: ct);
            var replicaSet = hello.TryGetValue("setName", out var setName);
            var sharded = hello.TryGetValue("msg", out var msg) && msg.IsString && msg.AsString == "isdbgrid";

            if (mode == MongoMode.Replica)
            {
                if (replicaSet)
                    app.Logger.LogInformation("MongoDB replica set '{ReplicaSet}' verified", setName.AsString);
                else if (sharded)
                    app.Logger.LogInformation("MongoDB sharded cluster verified");
                else
                    throw new InvalidOperationException(
                        "MONGO_MODE=replica requires a replica set (or sharded cluster): registration, comments, likes and cron leases use multi-document transactions. " +
                        "Start the bundled replica set with `docker compose -f docker-compose.dev.yml up -d mongo`, point DATABASE_URL at a replica-set member, " +
                        "or set MONGO_MODE=single to run without transactions (multi-document writes lose atomicity). " +
                        "The configured server reports as a standalone.");
            }
            else if (replicaSet && !new MongoUrl(url).DirectConnection)
                app.Logger.LogWarning("MONGO_MODE=single: connected to replica set '{ReplicaSet}' without directConnection; the driver will discover member host names, which may not resolve from this machine. Add directConnection=true to DATABASE_URL.", setName.AsString);
            else if (replicaSet)
                app.Logger.LogInformation("MONGO_MODE=single: connected to replica set '{ReplicaSet}'; transactions are disabled", setName.AsString);
            else if (sharded)
                app.Logger.LogInformation("MONGO_MODE=single: connected to a sharded cluster; transactions are disabled");
            else
                app.Logger.LogInformation("MONGO_MODE=single: connected to a standalone server; multi-document writes run without transactions");
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException or SocketException)
        {
            app.Logger.LogWarning(ex, "Could not verify MongoDB topology at startup; /health will report the connection state");
        }
    }
}
