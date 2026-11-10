using System.Security.Claims;
using Npgsql;
using Runwick;
using Runwick.Management;

namespace CarParker.Infrastructure.Setup;

public static class RunwickSetup
{
    public const string ApplicationName = "carparker-replicas";
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
                .UsePostgres(connectionString)
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
            dashboard.Title = "CarParker Replicas Jobs";
            dashboard.SignOutPath = RunwickManagementAuth.LogoutPath;
        });

        builder.Services.AddHealthChecks().AddRunwick();
        return builder;
    }

    public static string ConnectionString(IConfiguration config)
    {
        var value = config["Runwick:DatabaseUrl"] ?? config["RUNWICK_DATABASE_URL"]
            ?? throw new InvalidOperationException("RUNWICK_DATABASE_URL is required when Runwick is enabled.");
        return Parse(value);
    }

    private static string Parse(string value)
    {
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)) return value;

        var uri = new Uri(value);
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = uri.IdnHost,
            Port = uri.Port < 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath[1..]),
        };
        if (uri.UserInfo.Length > 0)
        {
            var credentials = uri.UserInfo.Split(':', 2);
            connection.Username = Uri.UnescapeDataString(credentials[0]);
            if (credentials.Length == 2) connection.Password = Uri.UnescapeDataString(credentials[1]);
        }
        if (uri.Query.Length > 1)
        {
            foreach (var pair in uri.Query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2) connection[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
            }
        }
        return connection.ConnectionString;
    }

    private static bool IsDashboardAdmin(ClaimsPrincipal principal, string username) =>
        principal.Identity?.IsAuthenticated == true
        && string.Equals(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, username, StringComparison.Ordinal);
}
