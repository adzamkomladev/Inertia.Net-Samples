using TechLandier.Data;
using TechLandier.Features;

namespace TechLandier.Infrastructure;

/// <summary>
/// Domain registrations shared by the web and worker roles. Inertia, endpoints, jobs consumers
/// and cron hosting stay in the hosts so Core never depends on their packages.
/// </summary>
public static class CoreSetup
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<RealtimeEvents>();
        services.AddSingleton<StockAlerts>();

        services.AddScoped<ManagementService>();
        services.AddScoped<StoreService>().AddScoped<CheckoutService>().AddScoped<OrderService>()
            .AddScoped<CareService>().AddScoped<CreditService>().AddScoped<DeliveriesService>()
            .AddScoped<SupportService>().AddScoped<ReviewsService>().AddScoped<AccountService>()
            .AddScoped<AlertsService>().AddScoped<AdminService>().AddScoped<SalesService>()
            .AddScoped<AuthService>().AddScoped<UploadsService>().AddScoped<CronTasks>();

        services.AddJobQueue();
        return services;
    }
}
