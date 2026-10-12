using Microsoft.Extensions.Http.Resilience;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Features;
using SvelteMongoReplicasBlog.Features.Discovery;

namespace SvelteMongoReplicasBlog.Infrastructure.Setup;

public static class FeatureSetup
{
    public static WebApplicationBuilder AddBlogFeatures(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        builder.AddReplicaServices();
        builder.Services.AddSingleton<RealtimeEvents>();
        var sampleNamespace = new OptionalServiceNamespace("SvelteMongoReplicasBlog", builder.Environment.EnvironmentName.ToLowerInvariant(), config["MEILISEARCH_INDEX"]);
        builder.Services.AddSingleton(sampleNamespace);
        if (RedisUrlSettings.FromConfiguration(config) is { } redis)
        {
            builder.Services.AddStackExchangeRedisCache(o =>
            {
                o.ConfigurationOptions = new StackExchange.Redis.ConfigurationOptions
                {
                    User = redis.Username, Password = redis.Password, DefaultDatabase = redis.Database, Ssl = redis.UseTls,
                    AbortOnConnectFail = false, ConnectTimeout = 1000, SyncTimeout = 1000, AsyncTimeout = 1000, ConnectRetry = 1,
                };
                o.ConfigurationOptions.EndPoints.Add(redis.Host, redis.Port);
            });
        }
        else builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSingleton(sp => new OptionalReadCache(sp.GetService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(), sampleNamespace));
        if (MeilisearchConnectionSettings.FromConfiguration(config) is { } meilisearch)
        {
            builder.Services.AddSingleton(meilisearch);
            builder.Services.AddHttpClient<MeilisearchTaskClient>(http => http.Timeout = TimeSpan.FromSeconds(30));
        }

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
        builder.Services.AddJobHandler<SvelteMongoReplicasBlog.Features.Discovery.RefreshInterestProfile, RefreshInterestProfileHandler>();
        builder.Services.AddScoped<DiscoveryService>();
        builder.Services.AddSingleton<DigestClock>();
        builder.Services.AddScoped<DigestService>();

        builder.Services.AddSingleton<ViewTracker>();
        builder.Services.AddHostedService<ViewEventWriter>();
        builder.Services.AddCron(config);
        builder.Services.AddSingleton<Storage>();
        // The job worker already retries; a retried POST could double-send an email.
        builder.Services.AddHttpClient<Mailer>().AddStandardResilienceHandler(o => o.Retry.DisableFor(HttpMethod.Post));
        return builder;
    }
}
