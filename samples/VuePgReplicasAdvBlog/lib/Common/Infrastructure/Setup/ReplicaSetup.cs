using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace VuePgReplicasAdvBlog.Infrastructure.Setup;

public static class ReplicaSetup
{
    public static string Namespace(IHostApplicationBuilder builder) =>
        builder.Configuration["Replicas:Namespace"] ?? $"VuePgReplicasAdvBlog:{builder.Environment.EnvironmentName.ToLowerInvariant()}";

    public static bool Enabled(IConfiguration config) => config.GetValue("Replicas:Enabled", true);

    public static TBuilder AddReplicaServices<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var signalR = builder.Services.AddSignalR();
        if (!Enabled(builder.Configuration)) return builder;
        var redis = RedisUrlSettings.FromConfiguration(builder.Configuration)
            ?? throw new InvalidOperationException("REDIS_URL is required for replica broadcasts and shared rate limits.");
        var options = new ConfigurationOptions
        {
            User = redis.Username,
            Password = redis.Password,
            DefaultDatabase = redis.Database,
            Ssl = redis.UseTls,
            AbortOnConnectFail = false,
            ConnectTimeout = 5000,
            SyncTimeout = 2000,
            AsyncTimeout = 2000,
        };
        options.EndPoints.Add(redis.Host, redis.Port);
        signalR.AddStackExchangeRedis(o =>
        {
            o.Configuration = options.Clone();
            o.Configuration.ChannelPrefix = RedisChannel.Literal(Namespace(builder) + ":signalr");
        });
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));
        builder.Services.AddHealthChecks().AddCheck<ReplicaRedisHealthCheck>("replica-redis");
        return builder;
    }
}

public sealed class ReplicaRedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) { return HealthCheckResult.Unhealthy("Replica Redis is unavailable.", ex); }
    }
}
