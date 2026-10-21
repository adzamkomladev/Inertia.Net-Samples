using Microsoft.Extensions.Http.Resilience;
using TechLandier.Features;

namespace TechLandier.Infrastructure.Setup;

public static class FeatureSetup
{
    public static WebApplicationBuilder AddStoreFeatures(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddSignalR();
        services.AddSingleton<RealtimeEvents>();
        services.AddSingleton<StockAlerts>();
        services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));

        services.AddScoped<StoreService>().AddScoped<CheckoutService>().AddScoped<OrderService>()
            .AddScoped<CareService>().AddScoped<CreditService>().AddScoped<DeliveriesService>()
            .AddScoped<SupportService>().AddScoped<ReviewsService>().AddScoped<AccountService>()
            .AddScoped<AlertsService>().AddScoped<AdminService>().AddScoped<SalesService>()
            .AddScoped<AuthService>().AddScoped<UploadsService>().AddScoped<CronTasks>();

        services.AddJobs();
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

        services.AddSingleton<ViewTracker>();
        services.AddHostedService<ViewEventWriter>();
        services.AddCron(builder.Configuration);
        services.AddSingleton<Storage>();
        // The job worker already retries; a retried POST could double-send an email.
        services.AddHttpClient<Mailer>().AddStandardResilienceHandler(o => o.Retry.DisableFor(HttpMethod.Post));
        return builder;
    }
}
