namespace VuePgReplicasAdvBlog.Infrastructure.Setup;

public static class CommonSetup
{
    public static TBuilder AddCommonInfrastructure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var config = builder.Configuration;
        builder.AddReplicaServices();
        builder.Services.AddSingleton<RealtimeEvents>();
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<JobQueue>();
        builder.Services.AddSingleton(new OptionalServiceNamespace("VuePgReplicasAdvBlog", builder.Environment.EnvironmentName.ToLowerInvariant(), config["MEILISEARCH_INDEX"]));
        if (RedisUrlSettings.FromConfiguration(config) is { } redis)
            builder.Services.AddStackExchangeRedisCache(o =>
            {
                o.ConfigurationOptions = new StackExchange.Redis.ConfigurationOptions
                {
                    User = redis.Username,
                    Password = redis.Password,
                    DefaultDatabase = redis.Database,
                    Ssl = redis.UseTls,
                    AbortOnConnectFail = false,
                    ConnectTimeout = 1000,
                    SyncTimeout = 1000,
                    AsyncTimeout = 1000,
                    ConnectRetry = 1,
                };
                o.ConfigurationOptions.EndPoints.Add(redis.Host, redis.Port);
            });
        builder.Services.AddSingleton(sp => new OptionalReadCache(sp.GetService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
            sp.GetRequiredService<OptionalServiceNamespace>()));
        return builder;
    }
}
