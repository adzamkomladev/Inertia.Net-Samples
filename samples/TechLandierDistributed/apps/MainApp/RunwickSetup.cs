using Runwick;
using TechLandier.Infrastructure;

namespace TechLandier.Infrastructure.Setup;

/// <summary>MainApp is a producer only: it dispatches jobs and never runs workers or the scheduler.</summary>
public static class RunwickSetup
{
    public const string DashboardPath = "/runwick";
    public const string ManagementPath = "/runwick-api";

    public static WebApplicationBuilder AddRunwickProducer(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        if (!RunwickDefaults.IsEnabled(config)) return builder;

        builder.Services.AddRunwick(runwick =>
        {
            runwick.Application(RunwickDefaults.ApplicationName)
                .UsePostgres(RunwickDefaults.ConnectionString(config))
                .AddRunwickJobs();
        });
        return builder;
    }

    public static bool IsEnabled(IConfiguration config) => RunwickDefaults.IsEnabled(config);
}
