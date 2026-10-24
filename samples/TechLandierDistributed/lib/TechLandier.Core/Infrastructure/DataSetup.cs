using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using TechLandier.Data;

namespace TechLandier.Infrastructure;

/// <summary>
/// Data and shared-state registrations used by every role. Web, worker and realtime all read the
/// same PostgreSQL database and Data Protection key ring; only the realtime-capable roles add a
/// SignalR backplane on top.
/// </summary>
public static class DataSetup
{
    public static TBuilder AddCoreData<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var config = builder.Configuration;
        var contentRoot = builder.Environment.ContentRootPath;
        var sampleRoot = File.Exists(Path.Combine(contentRoot, "..", "..", "TechLandierDistributed.slnx"))
            ? Path.GetFullPath(Path.Combine(contentRoot, "..", "..")) : contentRoot;
        var dataDir = Path.GetFullPath(config["App:DataDir"] is { Length: > 0 } d ? d : "storage", sampleRoot);
        Directory.CreateDirectory(dataDir);
        var connectionString = config["DATABASE_URL"] ?? config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("DATABASE_URL is required.");

        // The factory gives parallel Inertia loaders their own context; handlers and services share one scoped AppDb per request.
        builder.Services.AddDbContextFactory<AppDb>(o => o.UseNpgsql(DatabaseConnection.Parse(connectionString)));
        builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDb>>().CreateDbContext());
        builder.Services.AddSingleton<Storage>();
        builder.Services.AddSingleton<IReadDbFactory, ReadOnlyDbFactory>();

        var namespaceName = ReplicaSetup.Namespace(builder);
        var protection = builder.Services.AddDataProtection().SetApplicationName(namespaceName);
        if (ReplicaSetup.Enabled(config))
        {
            var redis = RedisUrlSettings.FromConfiguration(config)
                ?? throw new InvalidOperationException("REDIS_URL is required when Replicas:Enabled is true.");
            // AbortOnConnectFail=false: a temporary outage leaves the process up, reports unhealthy and reconnects.
            var multiplexer = ConnectionMultiplexer.Connect(redis.Options);
            builder.Services.AddSingleton(new CriticalRedisConnection(multiplexer));
            builder.Services.AddSingleton<IConnectionMultiplexer>(multiplexer);
            protection.PersistKeysToStackExchangeRedis(multiplexer, namespaceName + ":dataprotection");
            builder.Services.AddHealthChecks().AddCheck<RedisHealthCheck>("critical-redis", tags: ["redis"]);
        }
        else
        {
            // Single-process development and tests use the original file-backed key ring.
            protection.PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));
        }

        var health = builder.Services.AddHealthChecks().AddCheck("live", () => HealthCheckResult.Healthy(), tags: ["live"]);
        if (!string.IsNullOrWhiteSpace(config["Database:ReadUrl"]))
            health.AddCheck<ReadReplicaHealthCheck>("read-replica", tags: ["read-replica"]);
        return builder;
    }

    /// <summary>Adds SignalR and, for replicated deployments, the shared Redis backplane.</summary>
    public static IServiceCollection AddCoreRealtimeBackplane(this IServiceCollection services, IConfiguration config, IHostEnvironment environment)
    {
        var signalR = services.AddSignalR();
        var namespaceName = config["Replicas:Namespace"] ?? $"TechLandierDistributed:{environment.EnvironmentName.ToLowerInvariant()}";
        if (!ReplicaSetup.Enabled(config)) return services;
        var redis = RedisUrlSettings.FromConfiguration(config)
            ?? throw new InvalidOperationException("REDIS_URL is required for the SignalR backplane.");
        signalR.AddStackExchangeRedis(o =>
        {
            o.Configuration = redis.Options.Clone();
            o.Configuration.ChannelPrefix = RedisChannel.Literal(namespaceName + ":signalr");
        });
        return services;
    }
}

public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Critical Redis is unavailable.", ex);
        }
    }
}
