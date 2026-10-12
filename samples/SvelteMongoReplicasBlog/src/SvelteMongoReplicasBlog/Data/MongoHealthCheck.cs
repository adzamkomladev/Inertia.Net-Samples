using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace SvelteMongoReplicasBlog.Data;

public sealed class MongoHealthCheck(IMongoClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception e) { return HealthCheckResult.Unhealthy("MongoDB ping failed.", e); }
    }
}
