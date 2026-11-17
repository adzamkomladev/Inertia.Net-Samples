using Runwick;
using TechLandier.Infrastructure;

namespace TechLandier.Infrastructure.Setup;

/// <summary>
/// MainApp is a producer only: it stages and dispatches jobs (see <see cref="IJobOutbox"/>) and never runs workers,
/// handlers or the scheduler. The Worker role runs them.
/// </summary>
public static class RunwickSetup
{
    public static WebApplicationBuilder AddRunwickProducer(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var demoEnabled = RunwickDefaults.DemoEnabled(config, builder.Environment);

        builder.Services.AddRunwick(runwick =>
        {
            runwick.Application(RunwickDefaults.ApplicationName)
                .UsePostgres(RunwickDefaults.ConnectionString(config))
                .AddRunwickJobs(demoEnabled);
        });
        return builder;
    }
}
