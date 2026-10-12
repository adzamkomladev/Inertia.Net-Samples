using ReactSqliteBlog.Cron;
using ReactSqliteBlog.Features.Analytics;
using ReactSqliteBlog.Features.Auth;
using ReactSqliteBlog.Features.Discovery;
using ReactSqliteBlog.Features.Posts;
using ReactSqliteBlog.Features.Settings;
using ReactSqliteBlog.Features.Uploads;
using ReactSqliteBlog.Jobs;
using ReactSqliteBlog.Jobs.Handlers;
using Microsoft.Extensions.Caching.Distributed;

namespace ReactSqliteBlog.Infrastructure.Setup;

/// <summary>Feature registration: queue, cron, storage, social features and optional services.</summary>
public static class FeatureSetup
{
    public static WebApplicationBuilder AddBlogFeatures(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<RealtimeEvents>();
        var services = builder.Services;

        services.AddJobs();
        services.AddCron(config);
        services.AddStorage(config);
        services.AddAuthFeature(config);
        services.AddPostsFeature();
        services.AddScoped<DiscoveryService>();
        services.AddSingleton<DigestClock>();
        services.AddScoped<DigestService>();
        services.AddSingleton(new OptionalServiceNamespace("ReactSqliteBlog", builder.Environment.EnvironmentName.ToLowerInvariant(), config["MEILISEARCH_INDEX"]));

        if (RedisUrlSettings.FromConfiguration(config) is { } redis)
            services.AddStackExchangeRedisCache(o =>
            {
                o.ConfigurationOptions = new StackExchange.Redis.ConfigurationOptions
                {
                    User = redis.Username, Password = redis.Password, DefaultDatabase = redis.Database, Ssl = redis.UseTls,
                    AbortOnConnectFail = false, ConnectTimeout = 1000, SyncTimeout = 1000, AsyncTimeout = 1000, ConnectRetry = 1,
                };
                o.ConfigurationOptions.EndPoints.Add(redis.Host, redis.Port);
            });
        services.AddSingleton(sp => new OptionalReadCache(sp.GetService<IDistributedCache>(),
            sp.GetRequiredService<OptionalServiceNamespace>()));

        if (MeilisearchConnectionSettings.FromConfiguration(config) is { } meilisearch)
        {
            services.AddSingleton(meilisearch);
            services.AddHttpClient<MeilisearchTaskClient>(http => http.Timeout = TimeSpan.FromSeconds(30));
        }

        services.AddJobHandler<SyncSearchPost, SyncSearchPostHandler>();
        services.AddJobHandler<SyncSearchPostsBatch, SyncSearchPostsBatchHandler>();
        services.AddJobHandler<ReindexSearchBatch, ReindexSearchBatchHandler>();
        services.AddJobHandler<RefreshInterestProfile, RefreshInterestProfileHandler>();
        services.AddJobHandler<SendRecommendationDigest, SendRecommendationDigestHandler>();
        services.AddUploadsFeature();
        services.AddAnalyticsFeature();
        services.AddSettingsFeature();
        return builder;
    }
}
