using Microsoft.Extensions.Http.Resilience;
using System.Text.Json.Serialization.Metadata;
using Inertia.Net;
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
        services.AddCron(builder.Configuration);
        // The job worker already retries; a retried POST could double-send an email.
        services.AddHttpClient<Mailer>().AddStandardResilienceHandler(o => o.Retry.DisableFor(HttpMethod.Post));
        return builder;
    }
}
