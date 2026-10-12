using Microsoft.Extensions.Http.Resilience;
using VuePgReplicasAdvBlog.Features.Analytics;
using VuePgReplicasAdvBlog.Features.Comments;
using VuePgReplicasAdvBlog.Features.Digests;
using VuePgReplicasAdvBlog.Features.Discovery;
using VuePgReplicasAdvBlog.Features.Email;
using VuePgReplicasAdvBlog.Features.Likes;
using VuePgReplicasAdvBlog.Features.Search;
using VuePgReplicasAdvBlog.Features.Uploads;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Worker;

public static class WorkerSetup
{
    public static TBuilder AddWorkerServices<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var services = builder.Services;
        services.AddScoped<DiscoveryService>();
        services.AddSingleton<DigestClock>();
        services.AddScoped<DigestService>();
        services.AddSingleton<Storage>();
        if (MeilisearchConnectionSettings.FromConfiguration(builder.Configuration) is { } meilisearch)
        {
            services.AddSingleton(meilisearch);
            services.AddHttpClient<MeilisearchTaskClient>(http => http.Timeout = TimeSpan.FromSeconds(30));
        }
        services.AddJobs();
        services.AddJobHandler<SendEmail, SendEmailHandler>();
        services.AddJobHandler<NotifyComment, NotifyCommentHandler>();
        services.AddJobHandler<NotifyLike, NotifyLikeHandler>();
        services.AddJobHandler<DeleteAssets, DeleteAssetsHandler>();
        services.AddJobHandler<SyncSearchPost, SyncSearchPostHandler>();
        services.AddJobHandler<SyncSearchPostsBatch, SyncSearchPostsBatchHandler>();
        services.AddJobHandler<ReindexSearchBatch, ReindexSearchBatchHandler>();
        services.AddJobHandler<SendRecommendationDigest, SendRecommendationDigestHandler>();
        services.AddJobHandler<RefreshInterestProfile, RefreshInterestProfileHandler>();
        services.AddJobHandler<RecordView, RecordViewHandler>();
        services.AddCron(builder.Configuration);
        // Queue retries own delivery; retrying HTTP POST here could send an email twice.
        services.AddHttpClient<Mailer>().AddStandardResilienceHandler(o => o.Retry.DisableFor(HttpMethod.Post));
        return builder;
    }
}
