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
        var workerEnabled = config.GetValue("Jobs:Enabled", true);
        var concurrency = Math.Clamp(config.GetValue("Jobs:Concurrency", 4), 1, 32);
        var demoEnabled = DemoEnabled(config, builder.Environment);
        var credentials = RunwickManagementAuth.Credentials(config);
        RunwickManagementAuth.EnsureCredentialsAllowed(credentials, builder.Environment);

        builder.AddRunwickManagementAuth(credentials);

        builder.Services.AddRunwick(runwick =>
        {
            runwick.Application(ApplicationName)
                .UsePostgres(connectionString)
                .Queue("cron", q => q.MaxConcurrent(1))
                .Queue("mail")
                .Queue("events")
                .JobDefaults(j => j
                    .WithMaxExecutions(2)
                    .WithRetryDelay(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30)));

            // E-mail: the provider can be briefly down, so every mail job retries for about half an hour (five executions,
            // matching the retired Mongo queue) before giving up. A uniqueness key makes a repeated dispatch reuse the existing job.
            runwick.AddJob<SendVerificationEmail>().Named("mail.verification").ToQueue("mail").WithPriority("high")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).WithExecutionTimeout(TimeSpan.FromMinutes(1))
                .UniqueWhileActive(j => j.UserId.ToString("N"));
            // One reset mail per user while one is queued; a second request inside that time reuses it.
            runwick.AddJob<SendPasswordResetEmail>().Named("mail.password-reset").ToQueue("mail").WithPriority("high")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).WithExecutionTimeout(TimeSpan.FromMinutes(1))
                .UniqueWhileActive(j => j.UserId.ToString("N"));
            runwick.AddJob<NoShowEmail>().Named("mail.no-show").ToQueue("mail")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).WithExecutionTimeout(TimeSpan.FromMinutes(1))
                .UniqueWithinWindow(j => j.OccurrenceId.ToString("N"), TimeSpan.FromDays(30));
            runwick.AddJob<OverstayEmail>().Named("mail.overstay").ToQueue("mail").WithPriority("high")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).WithExecutionTimeout(TimeSpan.FromMinutes(1))
                .UniqueWithinWindow(j => j.SessionId.ToString("N"), TimeSpan.FromDays(30));
            runwick.AddJob<SessionEndedEmail>().Named("mail.session-ended").ToQueue("mail")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).WithExecutionTimeout(TimeSpan.FromMinutes(1))
                .UniqueWithinWindow(j => j.SessionId.ToString("N"), TimeSpan.FromDays(30));
            runwick.AddJob<RefundPaidEmail>().Named("mail.refund-paid").ToQueue("mail")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).WithExecutionTimeout(TimeSpan.FromMinutes(1))
                .UniqueWithinWindow(j => j.RefundId.ToString("N"), TimeSpan.FromDays(30));
            runwick.AddJob<BookingConfirmedEmail>().Named("mail.booking-confirmed").ToQueue("mail")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).WithExecutionTimeout(TimeSpan.FromMinutes(1))
                .UniqueWithinWindow(j => j.ReservationId.ToString("N"), TimeSpan.FromDays(30));
            runwick.AddJob<ReservationReminderEmail>().Named("mail.reservation-reminder").ToQueue("mail").WithPriority("low")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15)).WithExecutionTimeout(TimeSpan.FromMinutes(2))
                .UniqueWithinWindow(j => j.OccurrenceId.ToString("N"), TimeSpan.FromDays(2));
            runwick.AddJob<ClaimDecisionEmail>().Named("mail.claim-decision").ToQueue("mail")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).WithExecutionTimeout(TimeSpan.FromMinutes(1))
                .UniqueWithinWindow(j => $"{j.ClaimId:N}:{j.Decision}", TimeSpan.FromDays(30));
            // Object cleanup and realtime fan-out. Realtime is only worth delivering while it is fresh.
            runwick.AddJob<DeleteAssets>().Named("events.delete-assets").ToQueue("events").WithPriority("low")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10))
                .WithExecutionTimeout(TimeSpan.FromMinutes(2));
            runwick.AddJob<RealtimePush>().Named("events.realtime-push").ToQueue("events").WithPriority("high")
                .WithMaxExecutions(3).WithRetryDelay(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10))
                .WithExecutionTimeout(TimeSpan.FromSeconds(15));
            // Cron tasks are not retried; the next occurrence handles transient failures.
            runwick.AddJob<ScheduledCronJob>().Named("cron.scheduled").ToQueue("cron").WithPriority("low")
                .WithMaxExecutions(1).WithExecutionTimeout(TimeSpan.FromMinutes(30));

            runwick.AddHandler<SendVerificationEmail, SendVerificationEmailHandler>();
            runwick.AddHandler<SessionEndedEmail, SessionEndedEmailHandler>();
            runwick.AddHandler<SendPasswordResetEmail, SendPasswordResetEmailHandler>();
            runwick.AddHandler<NoShowEmail, NoShowEmailHandler>();
            runwick.AddHandler<OverstayEmail, OverstayEmailHandler>();
            runwick.AddHandler<RefundPaidEmail, RefundPaidEmailHandler>();
            runwick.AddHandler<BookingConfirmedEmail, BookingConfirmedEmailHandler>();
            runwick.AddHandler<ReservationReminderEmail, ReservationReminderEmailHandler>();
            runwick.AddHandler<ClaimDecisionEmail, ClaimDecisionEmailHandler>();
            runwick.AddHandler<DeleteAssets, DeleteAssetsHandler>();
            runwick.AddHandler<RealtimePush, RealtimePushHandler>();
            runwick.AddHandler<ScheduledCronJob, ScheduledCronJobHandler>();

            if (demoEnabled) AddDemoJobs(runwick);

            // A web replica can set JOBS_ENABLED=false: it still dispatches durably, another process runs the work.
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

    /// <summary>
    /// The demo sleep jobs, their queues and pools exist to show Runwick's dashboard under load. They are on in Development
    /// and off elsewhere unless Runwick:Demo:Enabled (RUNWICK_DEMO_ENABLED) says otherwise, so a production worker does not
    /// poll four idle queues and /demo/jobs is not mapped.
    /// </summary>
    public static bool DemoEnabled(IConfiguration config, IHostEnvironment environment) =>
        config.GetValue("Runwick:Demo:Enabled", environment.IsDevelopment());

    private static void AddDemoJobs(RunwickBuilder runwick)
    {
        runwick.Queue("demo-fast", q => q.MaxConcurrent(8))
            .Queue("demo-scaled")
            .Queue("demo-slow")
            .Queue("demo-long");
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
        runwick.AddHandler<DemoSleep100ms, DemoSleepHandler>();
        runwick.AddHandler<DemoSleep500ms, DemoSleepHandler>();
        runwick.AddHandler<DemoSleep2s, DemoSleepHandler>();
        runwick.AddHandler<DemoSleep10s, DemoSleepHandler>();
        runwick.AddHandler<DemoSleep30s, DemoSleepHandler>();
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
