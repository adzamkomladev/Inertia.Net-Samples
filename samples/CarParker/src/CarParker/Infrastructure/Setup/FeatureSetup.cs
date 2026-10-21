using Microsoft.Extensions.Http.Resilience;
using CarParker.Data;

namespace CarParker.Infrastructure.Setup;

public static class FeatureSetup
{
    public static WebApplicationBuilder AddBlogFeatures(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<RealtimeEvents>();
        var sampleNamespace = new OptionalServiceNamespace("CarParker", builder.Environment.EnvironmentName.ToLowerInvariant(), config["MEILISEARCH_INDEX"]);
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
        builder.Services.AddJobHandler<DeleteAssets, DeleteAssetsHandler>();
        builder.Services.AddJobHandler<RefundPaidEmail, RefundPaidEmailHandler>();
        builder.Services.AddJobHandler<ReservationReminderEmail, ReservationReminderEmailHandler>();
        builder.Services.AddJobHandler<ClaimDecisionEmail, ClaimDecisionEmailHandler>();
        // Registered unconditionally: handlers no-op when Meilisearch is not configured, so queued
        // sync jobs from a meili session are consumed (instead of erroring) in plain runs.
        builder.Services.AddJobHandler<SyncLot, SyncLotHandler>();
        builder.Services.AddJobHandler<SyncLotsBatch, SyncLotsBatchHandler>();
        builder.Services.AddJobHandler<ReindexSearchBatch, ReindexSearchBatchHandler>();
        builder.Services.AddCron(config);
        builder.Services.AddSingleton<Storage>();
        // The job worker already retries; a retried POST could double-send an email.
        builder.Services.AddHttpClient<Mailer>().AddStandardResilienceHandler(o => o.Retry.DisableFor(HttpMethod.Post));
        return builder;
    }
}
