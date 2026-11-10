using System.Security.Claims;
using Inkwell.Cron;
using Runwick;
using Runwick.Management;

namespace Inkwell.Infrastructure.Setup;

public static class RunwickSetup
{
    public const string ApplicationName = "inkwell";
    public const string DashboardPath = "/runwick";
    public const string ManagementPath = "/runwick-api";

    public static bool IsEnabled(IConfiguration config) => config.GetValue("Runwick:Enabled", true);

    public static WebApplicationBuilder AddStoreRunwick(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        if (!IsEnabled(config)) return builder;

        var connectionString = ConnectionString(config);
        var cronEnabled = config.GetValue("Cron:Enabled", true);
        var credentials = RunwickManagementAuth.Credentials(config);

        builder.AddRunwickManagementAuth(credentials);

        builder.Services.AddRunwick(runwick =>
        {
            runwick.Application(ApplicationName)
                .UseSqlite(connectionString)
                .Queue("cron", q => q.MaxConcurrent(1))
                .Queue("demo-fast", q => q.MaxConcurrent(8))
                .Queue("demo-scaled")
                .Queue("demo-slow")
                .Queue("demo-long")
                .JobDefaults(j => j
                    .WithMaxExecutions(2)
                    .WithRetryDelay(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30)));

            runwick.AddJob<DemoSleep100ms>().Named("demo.sleep.100ms").ToQueue("demo-fast").WithPriority("high")
                .WithExecutionTimeout(TimeSpan.FromSeconds(15)).MaxConcurrent(32);
            runwick.AddJob<DemoSleep500ms>().Named("demo.sleep.500ms").ToQueue("demo-scaled").WithPriority("medium")
                .WithExecutionTimeout(TimeSpan.FromSeconds(30)).MaxConcurrent(24);
            runwick.AddJob<DemoSleep2s>().Named("demo.sleep.2s").ToQueue("demo-scaled").WithPriority("medium")
                .WithExecutionTimeout(TimeSpan.FromMinutes(1)).MaxConcurrent(16);
            runwick.AddJob<DemoSleep10s>().Named("demo.sleep.10s").ToQueue("demo-slow").WithPriority("medium")
                .WithExecutionTimeout(TimeSpan.FromMinutes(2)).MaxConcurrent(8);
            runwick.AddJob<DemoSleep30s>().Named("demo.sleep.30s").ToQueue("demo-long").WithPriority("low")
                .WithExecutionTimeout(TimeSpan.FromMinutes(3)).MaxConcurrent(8);
            runwick.AddJob<ScheduledCronJob>().Named("cron.scheduled").ToQueue("cron").WithPriority("low")
                .WithMaxExecutions(1).WithExecutionTimeout(TimeSpan.FromMinutes(30));

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

            if (cronEnabled)
            {
                runwick.AddScheduler();
                runwick.AddCronSchedules(config);
            }
        });

        var grants = new List<ManagementGrant>(ManagementRoles.Administrator)
        {
            new(ManagementPermissions.JobsReadPayload),
            new(ManagementPermissions.JobsReadError),
            new(ManagementPermissions.JobsDispatch),
            new(ManagementPermissions.AuditRead),
        };
        builder.Services.AddRunwickManagement(management => management
            .AuthenticationSchemes(RunwickManagementAuth.Scheme)
            .MapGrants(principal => IsDashboardAdmin(principal, credentials.Username) ? grants : []));

        builder.Services.AddRunwickDashboard("default", dashboard =>
        {
            dashboard.Title = "Inkwell Jobs";
            dashboard.SignOutPath = RunwickManagementAuth.LogoutPath;
        });

        builder.Services.AddHealthChecks().AddRunwick();
        return builder;
    }

    /// <summary>The Runwick tables share the app's SQLite file; the migrator ignores non-runwick tables.</summary>
    public static string ConnectionString(IConfiguration config) =>
        config["DATABASE_URL"] ?? config.GetConnectionString("Default") ?? "Data Source=/data/app.db";

    private static bool IsDashboardAdmin(ClaimsPrincipal principal, string username) =>
        principal.Identity?.IsAuthenticated == true
        && string.Equals(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, username, StringComparison.Ordinal);
}
