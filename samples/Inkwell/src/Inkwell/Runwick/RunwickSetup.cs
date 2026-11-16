using System.Security.Claims;
using Inkwell.Cron;
using Inkwell.Jobs;
using Inkwell.Jobs.Handlers;
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
        var workerEnabled = config.GetValue("Jobs:Enabled", true);
        var concurrency = Math.Clamp(config.GetValue("Jobs:Concurrency", 4), 1, 32);
        var demoEnabled = DemoEnabled(config, builder.Environment);
        var credentials = RunwickManagementAuth.Credentials(config);

        builder.AddRunwickManagementAuth(credentials);

        builder.Services.AddRunwick(runwick =>
        {
            runwick.Application(ApplicationName)
                .UseSqlite(connectionString)
                .Queue("cron", q => q.MaxConcurrent(1))
                .Queue("mail")
                .Queue("events")
                .JobDefaults(j => j
                    .WithMaxExecutions(2)
                    .WithRetryDelay(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30)));

            // E-mail: the mail provider can be briefly down, so retry for about half an hour before giving up.
            runwick.AddJob<SendVerificationEmail>().Named("mail.verification").ToQueue("mail").WithPriority("high")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10))
                .WithExecutionTimeout(TimeSpan.FromMinutes(1))
                .UniqueWhileActive(j => j.UserId.ToString("N"));
            runwick.AddJob<CommentNotification>().Named("mail.comment").ToQueue("mail")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10))
                .WithExecutionTimeout(TimeSpan.FromMinutes(1));
            runwick.AddJob<LikeNotification>().Named("mail.like").ToQueue("mail").WithPriority("low")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10))
                .WithExecutionTimeout(TimeSpan.FromMinutes(1))
                .UniqueWhileActive(j => $"{j.PostId:N}:{j.Window}");
            runwick.AddJob<WeeklyDigestEmail>().Named("mail.weekly-digest").ToQueue("mail").WithPriority("low")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15))
                .WithExecutionTimeout(TimeSpan.FromMinutes(2))
                .UniqueWithinWindow(j => $"{j.UserId:N}:{j.Week}", TimeSpan.FromDays(8));
            runwick.AddJob<SendRecommendationDigest>().Named("mail.recommendation-digest").ToQueue("mail").WithPriority("low")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15))
                .WithExecutionTimeout(TimeSpan.FromMinutes(2))
                .UniqueWhileActive(j => j.DeliveryId.ToString("N"));
            // Realtime fan-out is only worth doing while it is fresh.
            runwick.AddJob<PostChanged>().Named("events.post-changed").ToQueue("events").WithPriority("high")
                .WithMaxExecutions(3).WithRetryDelay(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10))
                .WithExecutionTimeout(TimeSpan.FromSeconds(15));
            runwick.AddJob<PostRemoved>().Named("events.post-removed").ToQueue("events").WithPriority("high")
                .WithMaxExecutions(3).WithRetryDelay(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10))
                .WithExecutionTimeout(TimeSpan.FromSeconds(15));
            runwick.AddJob<ScheduledCronJob>().Named("cron.scheduled").ToQueue("cron").WithPriority("low")
                .WithMaxExecutions(1).WithExecutionTimeout(TimeSpan.FromMinutes(30));

            runwick.AddHandler<SendVerificationEmail, SendVerificationEmailHandler>();
            runwick.AddHandler<CommentNotification, CommentNotificationHandler>();
            runwick.AddHandler<LikeNotification, LikeNotificationHandler>();
            runwick.AddHandler<WeeklyDigestEmail, WeeklyDigestEmailHandler>();
            runwick.AddHandler<SendRecommendationDigest, SendRecommendationDigestHandler>();
            runwick.AddHandler<PostChanged, PostChangedHandler>();
            runwick.AddHandler<PostRemoved, PostRemovedHandler>();
            runwick.AddHandler<ScheduledCronJob, ScheduledCronJobHandler>();

            if (demoEnabled) AddDemoJobs(runwick);

            // A web replica can set JOBS_ENABLED=false: it still dispatches durably, another process runs the work.
            if (workerEnabled)
                runwick.AddWorker(worker =>
                {
                    // Must cover the sum of pool maxima: app + cron (+ the 4 + 8 + 4 + 4 demo pools).
                    worker.MaxOutstandingAttempts(concurrency + 1 + (demoEnabled ? 20 : 0));
                    worker.Pool("app", p => p.Queues("events", "mail").AutoConcurrency(1, concurrency));
                    worker.Pool("cron-fixed", p => p.Queues("cron").FixedConcurrency(1));
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
            dashboard.Title = "Inkwell Jobs";
            dashboard.SignOutPath = RunwickManagementAuth.LogoutPath;
        });

        builder.Services.AddHealthChecks().AddRunwick();
        return builder;
    }

    /// <summary>
    /// The demo sleep jobs, their queues and their four pools exist to show Runwick's dashboard under load. They are
    /// on in Development and off elsewhere unless Runwick:Demo:Enabled (RUNWICK_DEMO_ENABLED) says otherwise, so a
    /// production worker does not poll four idle queues.
    /// </summary>
    public static bool DemoEnabled(IConfiguration config, IHostEnvironment environment) =>
        IsEnabled(config) && config.GetValue("Runwick:Demo:Enabled", environment.IsDevelopment());

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

    /// <summary>The Runwick tables share the app's SQLite file; the migrator ignores non-runwick tables.</summary>
    public static string ConnectionString(IConfiguration config) =>
        config["DATABASE_URL"] ?? config.GetConnectionString("Default") ?? "Data Source=/data/app.db";

    private static bool IsDashboardAdmin(ClaimsPrincipal principal, string username) =>
        principal.Identity?.IsAuthenticated == true
        && string.Equals(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, username, StringComparison.Ordinal);
}
