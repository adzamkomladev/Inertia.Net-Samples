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
        // Folds bursts of admins-pulse publishes into one trailing reload job. Only this role runs the realtime
        // handlers, so the gate (which touches IInertiaLive) lives here rather than in Core.
        services.AddSingleton<AdminPulseGate>();
        services.AddStoreCache(builder.Configuration, builder.Environment).AddWorkerPropInvalidator();

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
        AddRunwickServices(services, builder.Configuration, builder.Environment);
        // Runwick already retries; a retried POST could double-send an email.
        services.AddHttpClient<Mailer>().AddStandardResilienceHandler(o => o.Retry.DisableFor(HttpMethod.Post));
        return builder;
    }

    /// <summary>
    /// Every Worker process dispatches (cron tasks and handlers stage follow-up jobs), runs handlers and, unless
    /// JOBS_ENABLED=false, polls the queues. CRON_ENABLED decides whether this process also runs the scheduler.
    /// </summary>
    private static void AddRunwickServices(IServiceCollection services, IConfiguration config, IHostEnvironment environment)
    {
        var workerEnabled = config.GetValue("Jobs:Enabled", true);
        var concurrency = Math.Clamp(config.GetValue("Jobs:Concurrency", 4), 1, 32);
        var demoEnabled = RunwickDefaults.DemoEnabled(config, environment);

        services.AddRunwick(runwick =>
        {
            runwick.Application(RunwickDefaults.ApplicationName)
                .UsePostgres(RunwickDefaults.ConnectionString(config))
                .AddRunwickJobs(demoEnabled);

            runwick.AddHandler<SendVerificationEmail, SendVerificationEmailHandler>();
            runwick.AddHandler<OrderPaidEmail, OrderPaidEmailHandler>();
            runwick.AddHandler<OrderShippedEmail, OrderShippedEmailHandler>();
            runwick.AddHandler<OrderCancelledEmail, OrderCancelledEmailHandler>();
            runwick.AddHandler<OrderRefundEmail, OrderRefundEmailHandler>();
            runwick.AddHandler<InstallmentPaidEmail, InstallmentPaidEmailHandler>();
            runwick.AddHandler<InstallmentReminderEmail, InstallmentReminderEmailHandler>();
            runwick.AddHandler<InstallmentStatementEmail, InstallmentStatementEmailHandler>();
            runwick.AddHandler<TicketReplyEmail, TicketReplyEmailHandler>();
            runwick.AddHandler<CareDecisionEmail, CareDecisionEmailHandler>();
            runwick.AddHandler<DeleteAssets, DeleteAssetsHandler>();
            runwick.AddHandler<RealtimePush, RealtimePushHandler>();
            runwick.AddHandler<PulseReload, PulseReloadHandler>();
            runwick.AddHandler<ScheduledCronJob, ScheduledCronJobHandler>();
            if (demoEnabled)
            {
                runwick.AddHandler<DemoSleep100ms, DemoSleepHandler>();
                runwick.AddHandler<DemoSleep500ms, DemoSleepHandler>();
                runwick.AddHandler<DemoSleep2s, DemoSleepHandler>();
                runwick.AddHandler<DemoSleep10s, DemoSleepHandler>();
                runwick.AddHandler<DemoSleep30s, DemoSleepHandler>();
            }

            if (workerEnabled)
                runwick.AddWorker(worker =>
                {
                    // Must cover the sum of pool maxima: events + mail + cron (+ the 4 + 8 + 4 + 4 demo pools).
                    worker.MaxOutstandingAttempts(2 * concurrency + 1 + (demoEnabled ? 20 : 0));
                    worker.Pool("events", p => p.Queues("events").AutoConcurrency(1, concurrency));
                    worker.Pool("mail", p => p.Queues("mail").AutoConcurrency(1, concurrency));
                    worker.Pool("cron", p => p.Queues("cron").FixedConcurrency(1));
                    if (demoEnabled)
                    {
                        worker.Pool("fast-fixed", p => p.Queues("demo-fast").FixedConcurrency(4));
                        worker.Pool("scaled-auto", p => p.Queues("demo-scaled").AutoConcurrency(1, 8));
                        worker.Pool("slow-auto", p => p.Queues("demo-slow").AutoConcurrency(1, 4));
                        worker.Pool("long-auto", p => p.Queues("demo-long").AutoConcurrency(1, 4));
                    }
                });

            if (config.GetValue("Cron:Enabled", true))
            {
                runwick.AddScheduler();
                runwick.AddCronSchedules(config);
            }
        });
    }
}