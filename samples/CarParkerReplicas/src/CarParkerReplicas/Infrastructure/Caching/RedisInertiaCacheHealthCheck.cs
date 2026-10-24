using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace CarParker.Infrastructure.Caching;

/// <summary>
/// The Inertia cache shares the sample's Redis. A cache outage degrades page performance, not
/// correctness, so this check is Degraded; the critical Redis check registered by ReplicaSetup is
/// Unhealthy and keeps the replica out of rotation when the shared state itself is unavailable.
/// </summary>
public sealed class RedisInertiaCacheHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync().WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Degraded(
                "The Inertia cache Redis is unavailable; pages fall back to the database and SSR.", exception);
        }
    }
}
