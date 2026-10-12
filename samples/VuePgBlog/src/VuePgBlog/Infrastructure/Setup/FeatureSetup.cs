using Microsoft.Extensions.Http.Resilience;
using VuePgBlog.Data;
using VuePgBlog.Features;
using VuePgBlog.Features.Discovery;

namespace VuePgBlog.Infrastructure.Setup;

public static class FeatureSetup
{
    public static WebApplicationBuilder AddBlogFeatures(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<RealtimeEvents>();
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
        builder.Services.AddJobs();
        builder.Services.AddJobHandler<SendEmail, SendEmailHandler>();
        builder.Services.AddJobHandler<NotifyComment, NotifyCommentHandler>();
        builder.Services.AddJobHandler<NotifyLike, NotifyLikeHandler>();
        builder.Services.AddJobHandler<DeleteAssets, DeleteAssetsHandler>();
        builder.Services.AddJobHandler<SyncSearchPost, SyncSearchPostHandler>();
        builder.Services.AddJobHandler<SyncSearchPostsBatch, SyncSearchPostsBatchHandler>();
        builder.Services.AddJobHandler<ReindexSearchBatch, ReindexSearchBatchHandler>();
        builder.Services.AddJobHandler<SendRecommendationDigest, SendRecommendationDigestHandler>();
        builder.Services.AddJobHandler<VuePgBlog.Features.Discovery.RefreshInterestProfile, RefreshInterestProfileHandler>();
        builder.Services.AddScoped<VuePgBlog.Features.Discovery.DiscoveryService>();
        builder.Services.AddSingleton<DigestClock>();
        builder.Services.AddScoped<DigestService>();
        builder.Services.AddSingleton(new OptionalServiceNamespace("VuePgBlog", builder.Environment.EnvironmentName.ToLowerInvariant(), config["MEILISEARCH_INDEX"]));
        if (RedisUrlSettings.FromConfiguration(config) is { } redis)
            builder.Services.AddStackExchangeRedisCache(o =>
            {
                o.ConfigurationOptions = new StackExchange.Redis.ConfigurationOptions
                {
                    User = redis.Username, Password = redis.Password, DefaultDatabase = redis.Database, Ssl = redis.UseTls,
                    AbortOnConnectFail = false, ConnectTimeout = 1000, SyncTimeout = 1000, AsyncTimeout = 1000, ConnectRetry = 1,
                };
                o.ConfigurationOptions.EndPoints.Add(redis.Host, redis.Port);
            });
        builder.Services.AddSingleton(sp => new OptionalReadCache(sp.GetService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
            sp.GetRequiredService<OptionalServiceNamespace>()));
        if (MeilisearchConnectionSettings.FromConfiguration(config) is { } meilisearch)
        {
            builder.Services.AddSingleton(meilisearch);
            builder.Services.AddHttpClient<MeilisearchTaskClient>(http => http.Timeout = TimeSpan.FromSeconds(30));
        }


        builder.Services.AddSingleton<ViewTracker>();
        builder.Services.AddHostedService<ViewEventWriter>();
        builder.Services.AddCron(config);
        builder.Services.AddSingleton<Storage>();
        // The job worker already retries; a retried POST could double-send an email.
        builder.Services.AddHttpClient<Mailer>().AddStandardResilienceHandler(o => o.Retry.DisableFor(HttpMethod.Post));
        return builder;
    }
}
