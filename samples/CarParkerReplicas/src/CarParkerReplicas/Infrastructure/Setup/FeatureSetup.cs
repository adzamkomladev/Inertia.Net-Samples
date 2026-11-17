using Microsoft.Extensions.Http.Resilience;
using CarParker.Features;
using CarParker.Features.Admin;
using CarParker.Features.Alerts;
using CarParker.Features.Analytics;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
using CarParker.Features.Insurance;
using CarParker.Features.Lots;

namespace CarParker.Infrastructure.Setup;

public static class FeatureSetup
{
    public static WebApplicationBuilder AddBlogFeatures(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        builder.AddReplicaServices();
        services.AddSingleton<RealtimeEvents>();
        services.AddScoped<AlertsService>();
        services.AddScoped<AdminService>();
        services.AddScoped<TeamService>();
        services.AddScoped<AuthService>();
        services.AddScoped<BookingsService>();
        services.AddScoped<ReceiptsService>();
        services.AddScoped<GuardService>();
        services.AddScoped<InsuranceService>();
        services.AddScoped<LotAnalyticsService>();
        services.AddScoped<LotsService>();
        services.AddScoped<PreferencesService>();
        services.AddScoped<UploadsService>();

        services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
        // Background work runs on Runwick (RunwickSetup registers the jobs and handlers).
        services.AddSingleton<JobEnqueuer>();
        services.AddSingleton<PrincipalCache>();
        services.AddMemoryCache();
        services.AddSingleton<Storage>();
        // Runwick already retries; a retried POST could double-send an email.
        services.AddHttpClient<Mailer>().AddStandardResilienceHandler(o => o.Retry.DisableFor(HttpMethod.Post));
        return builder;
    }
}
