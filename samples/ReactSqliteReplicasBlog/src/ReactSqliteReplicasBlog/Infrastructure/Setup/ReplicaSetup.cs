using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;

namespace ReactSqliteReplicasBlog.Infrastructure.Setup;

public static class ReplicaSetup
{
    public static string Namespace(WebApplicationBuilder builder) =>
        builder.Configuration["Replicas:Namespace"] ?? $"ReactSqliteReplicasBlog:{builder.Environment.EnvironmentName.ToLowerInvariant()}";

    public static bool Enabled(IConfiguration config) => config.GetValue("Replicas:Enabled", true);

    public static WebApplicationBuilder AddReplicaServices(this WebApplicationBuilder builder)
    {
        var signalR = builder.Services.AddSignalR();
        if (!Enabled(builder.Configuration)) return builder;
        var redis = RedisUrlSettings.FromConfiguration(builder.Configuration)
            ?? throw new InvalidOperationException("REDIS_URL is required for replica broadcasts and shared rate limits.");
        var options = new ConfigurationOptions
        {
            User = redis.Username, Password = redis.Password, DefaultDatabase = redis.Database, Ssl = redis.UseTls,
            AbortOnConnectFail = false, ConnectTimeout = 5000, SyncTimeout = 2000, AsyncTimeout = 2000,
        };
        options.EndPoints.Add(redis.Host, redis.Port);
        signalR.AddStackExchangeRedis(o =>
        {
            o.Configuration = options.Clone();
            o.Configuration.ChannelPrefix = RedisChannel.Literal(Namespace(builder) + ":signalr");
        });
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));
        builder.Services.AddDataProtection().SetApplicationName(Namespace(builder));
        builder.Services.AddOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>()
            .Configure<IConnectionMultiplexer>((o, connection) => o.XmlRepository =
                new Microsoft.AspNetCore.DataProtection.StackExchangeRedis.RedisXmlRepository(
                    () => connection.GetDatabase(), Namespace(builder) + ":keys"));
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
