using Microsoft.Extensions.Http.Resilience;
using System.Text.Json.Serialization.Metadata;
using TechLandier.Features;

namespace TechLandier.Infrastructure.Setup;

public static class FeatureSetup
{
    public static WebApplicationBuilder AddStoreFeatures(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddSignalR().AddJsonProtocol(o =>
        {
            // The live-props transport (InertiaSetup) pins its source-generated context into the hub payload
            // resolver chain. Keep a reflection resolver behind it so this app's own hubs keep sending typed payloads.
            var chain = o.PayloadSerializerOptions.TypeInfoResolverChain;
            if (!chain.OfType<DefaultJsonTypeInfoResolver>().Any()) chain.Add(new DefaultJsonTypeInfoResolver());
        });
        services.AddSingleton<RealtimeEvents>();
        services.AddSingleton<StockAlerts>();
        services.AddSingleton<AdminPulseGate>();
        services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));

        services.AddScoped<ManagementService>().AddScoped<DocumentsService>()
            .AddScoped<EmailUnsubscribe>().AddScoped<EmailContexts>();
        services.AddScoped<StoreService>().AddScoped<CheckoutService>().AddScoped<OrderService>()
            .AddScoped<CareService>().AddScoped<CreditService>().AddScoped<DeliveriesService>()
            .AddScoped<SupportService>().AddScoped<ReviewsService>().AddScoped<AccountService>()
            .AddScoped<AlertsService>().AddScoped<AdminService>().AddScoped<SalesService>()
            .AddScoped<AuthService>().AddScoped<UploadsService>().AddScoped<CronTasks>();

        // Background work runs on Runwick (RunwickSetup registers the jobs and handlers).
        services.AddSingleton<IJobOutbox, RunwickJobOutbox>();

        services.AddSingleton<ViewTracker>();
        services.AddHostedService<ViewEventWriter>();
        services.AddSingleton<Storage>();
        // Runwick already retries; a retried POST could double-send an email.
        services.AddHttpClient<Mailer>().AddStandardResilienceHandler(o => o.Retry.DisableFor(HttpMethod.Post));
        return builder;
    }
}
