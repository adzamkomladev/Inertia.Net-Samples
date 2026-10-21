using Microsoft.Extensions.Http.Resilience;
using VuePgBlog.Data;
using VuePgBlog.Features;

namespace VuePgBlog.Infrastructure.Setup;

public static class FeatureSetup
{
    public static WebApplicationBuilder AddStoreFeatures(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<RealtimeEvents>();
        builder.Services.AddSingleton<StockAlerts>();
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
        builder.Services.AddJobs();
        builder.Services.AddJobHandler<SendEmail, SendEmailHandler>();
        builder.Services.AddJobHandler<DeleteAssets, DeleteAssetsHandler>();
        builder.Services.AddJobHandler<WelcomeEmail, WelcomeEmailHandler>();
        builder.Services.AddJobHandler<OrderPaidEmail, OrderPaidEmailHandler>();
        builder.Services.AddJobHandler<OrderShippedEmail, OrderShippedEmailHandler>();
        builder.Services.AddJobHandler<OrderCancelledEmail, OrderCancelledEmailHandler>();
        builder.Services.AddJobHandler<OrderRefundEmail, OrderRefundEmailHandler>();
        builder.Services.AddJobHandler<InstallmentPaidEmail, InstallmentPaidEmailHandler>();
        builder.Services.AddJobHandler<InstallmentReminderEmail, InstallmentReminderEmailHandler>();
        builder.Services.AddJobHandler<TicketReplyEmail, TicketReplyEmailHandler>();
        builder.Services.AddJobHandler<CareDecisionEmail, CareDecisionEmailHandler>();
        builder.Services.AddJobHandler<SyncSearchProduct, SyncSearchProductHandler>();
        builder.Services.AddJobHandler<SyncSearchProductsBatch, SyncSearchProductsBatchHandler>();
        builder.Services.AddJobHandler<ReindexSearchBatch, ReindexSearchBatchHandler>();
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
