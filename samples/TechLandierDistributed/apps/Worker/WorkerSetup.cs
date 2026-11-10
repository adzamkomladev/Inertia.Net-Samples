using Microsoft.Extensions.Http.Resilience;
using System.Text.Json.Serialization.Metadata;
using Inertia.Net;
using Runwick;
using TechLandier.Caching;
using TechLandier.Infrastructure;

namespace TechLandier.Worker;

public static class WorkerSetup
{
    public static TBuilder AddWorkerServices<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var services = builder.Services;
        builder.AddCoreData();
        services.AddCoreServices();
        // Publishes IHubContext<StoreHub> messages through Redis; this process never maps a hub.
        services.AddCoreRealtimeBackplane(builder.Configuration, builder.Environment);
        services.AddStoreCache(builder.Configuration, builder.Environment).AddWorkerPropInvalidator();

        services.AddJobQueue();
        services.AddHostedService<JobWorker>();
        // Live props: the handler below publishes what MainApp pages listen for; this process never maps a hub.
        // The SignalR live transport joins the same backplane, and the reflection resolver stays behind its
        // source-generated JSON context so the StoreHub's pushed payloads keep serializing.
        // Inertia.Net 1.6.3: the live publisher needs the page writer, which AddInertia registers; the worker
        // itself never renders pages and no SSR or Vite configuration belongs here.
        services.AddInertia();
        services.AddInertiaLive().AddSignalR();
        services.AddSignalR().AddJsonProtocol(o =>
        {
            var chain = o.PayloadSerializerOptions.TypeInfoResolverChain;
            if (!chain.OfType<DefaultJsonTypeInfoResolver>().Any()) chain.Add(new DefaultJsonTypeInfoResolver());
        });
        services.AddJobHandler<RealtimePush, RealtimePushHandler>();
        services.AddJobHandler<SendEmail, SendEmailHandler>();
        services.AddJobHandler<DeleteAssets, DeleteAssetsHandler>();
        services.AddJobHandler<WelcomeEmail, WelcomeEmailHandler>();
        services.AddJobHandler<OrderPaidEmail, OrderPaidEmailHandler>();
        services.AddJobHandler<OrderShippedEmail, OrderShippedEmailHandler>();
        services.AddJobHandler<OrderCancelledEmail, OrderCancelledEmailHandler>();
        services.AddJobHandler<OrderRefundEmail, OrderRefundEmailHandler>();
        services.AddJobHandler<InstallmentPaidEmail, InstallmentPaidEmailHandler>();
        services.AddJobHandler<InstallmentReminderEmail, InstallmentReminderEmailHandler>();
        services.AddJobHandler<TicketReplyEmail, TicketReplyEmailHandler>();
        services.AddJobHandler<CareDecisionEmail, CareDecisionEmailHandler>();
        if (RunwickDefaults.IsEnabled(builder.Configuration)) AddRunwickServices(services, builder.Configuration);
        // The job worker already retries; a retried POST could double-send an email.
        services.AddHttpClient<Mailer>().AddStandardResilienceHandler(o => o.Retry.DisableFor(HttpMethod.Post));
        return builder;
    }

    private static void AddRunwickServices(IServiceCollection services, IConfiguration config)
    {
        services.AddRunwick(runwick =>
        {
            runwick.Application(RunwickDefaults.ApplicationName)
                .UsePostgres(RunwickDefaults.ConnectionString(config))
                .AddRunwickJobs();

            runwick.AddHandler<DemoSleep100ms, DemoSleepHandler>();
            runwick.AddHandler<DemoSleep500ms, DemoSleepHandler>();
            runwick.AddHandler<DemoSleep2s, DemoSleepHandler>();
            runwick.AddHandler<DemoSleep10s, DemoSleepHandler>();
            runwick.AddHandler<DemoSleep30s, DemoSleepHandler>();
            runwick.AddHandler<ScheduledCronJob, ScheduledCronJobHandler>();

            runwick.AddWorker(worker =>
            {
                worker.MaxOutstandingAttempts(24);
                worker.Pool("fast-fixed", p => p.Queues("demo-fast").FixedConcurrency(4));
                worker.Pool("scaled-auto", p => p.Queues("demo-scaled").AutoConcurrency(1, 8));
                worker.Pool("slow-auto", p => p.Queues("demo-slow").AutoConcurrency(1, 4));
                worker.Pool("long-auto", p => p.Queues("demo-long").AutoConcurrency(1, 4));
                worker.Pool("cron-fixed", p => p.Queues("cron").FixedConcurrency(1));
            });

            if (config.GetValue("Cron:Enabled", true))
            {
                runwick.AddScheduler();
                runwick.AddCronSchedules(config);
            }
        });
    }
}
