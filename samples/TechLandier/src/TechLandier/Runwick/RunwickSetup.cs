using System.Security.Claims;
using Runwick;
using Runwick.Management;
using TechLandier.Data;

namespace TechLandier.Infrastructure.Setup;

public static class RunwickSetup
{
    /// <summary>The application name stored in Runwick's tables. Stable so redeploys reuse the same queues and schedules.</summary>
    public const string ApplicationName = "techlandier";

    public const string DashboardPath = "/runwick";
    public const string ManagementPath = "/runwick-api";

    public static WebApplicationBuilder AddStoreRunwick(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
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

            // E-mail: the provider can be briefly down, so every mail job retries for about half an hour before giving
            // up. Each uniqueness key makes a repeated dispatch (a cron re-run, a double click) reuse the existing job.
            runwick.AddJob<SendVerificationEmail>().Named("mail.verification").ToQueue("mail").WithPriority("high")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).WithExecutionTimeout(TimeSpan.FromMinutes(1))
                .UniqueWhileActive(j => j.UserId.ToString("N"));
            runwick.AddJob<OrderPaidEmail>().Named("mail.order-paid").ToQueue("mail").WithPriority("high")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).WithExecutionTimeout(TimeSpan.FromMinutes(1))
                .UniqueWithinWindow(j => j.OrderId.ToString("N"), TimeSpan.FromDays(30));
            runwick.AddJob<OrderShippedEmail>().Named("mail.order-shipped").ToQueue("mail")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).WithExecutionTimeout(TimeSpan.FromMinutes(1))
                .UniqueWithinWindow(j => j.OrderId.ToString("N"), TimeSpan.FromDays(30));
            runwick.AddJob<OrderCancelledEmail>().Named("mail.order-cancelled").ToQueue("mail")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).WithExecutionTimeout(TimeSpan.FromMinutes(1))
                .UniqueWithinWindow(j => j.OrderId.ToString("N"), TimeSpan.FromDays(30));
            runwick.AddJob<OrderRefundEmail>().Named("mail.order-refund").ToQueue("mail")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).WithExecutionTimeout(TimeSpan.FromMinutes(1))
                .UniqueWithinWindow(j => j.OrderId.ToString("N"), TimeSpan.FromDays(30));
            runwick.AddJob<InstallmentPaidEmail>().Named("mail.installment-paid").ToQueue("mail")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).WithExecutionTimeout(TimeSpan.FromMinutes(1))
                .UniqueWithinWindow(j => j.InstallmentId.ToString("N"), TimeSpan.FromDays(30));
            runwick.AddJob<InstallmentReminderEmail>().Named("mail.installment-reminder").ToQueue("mail").WithPriority("low")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15)).WithExecutionTimeout(TimeSpan.FromMinutes(2))
                .UniqueWithinWindow(j => $"{j.PlanId:N}:{j.Kind}:{j.Day}", TimeSpan.FromDays(2));
            runwick.AddJob<InstallmentStatementEmail>().Named("mail.installment-statement").ToQueue("mail").WithPriority("low")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15)).WithExecutionTimeout(TimeSpan.FromMinutes(2))
                .UniqueWithinWindow(j => $"{j.UserId:N}:{j.Day}", TimeSpan.FromDays(2));
            runwick.AddJob<TicketReplyEmail>().Named("mail.ticket-reply").ToQueue("mail")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).WithExecutionTimeout(TimeSpan.FromMinutes(1));
            // Not unique: each staff reply or decision is its own event, staged once in its own transaction. A key per
            // ticket/claim would let an update that lands while the previous email is sending reuse that job and be lost.
            runwick.AddJob<CareDecisionEmail>().Named("mail.care-decision").ToQueue("mail")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)).WithExecutionTimeout(TimeSpan.FromMinutes(1));
            // Object cleanup and realtime fan-out. Realtime is only worth delivering while it is fresh.
            runwick.AddJob<DeleteAssets>().Named("events.delete-assets").ToQueue("events").WithPriority("low")
                .WithMaxExecutions(5).WithRetryDelay(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10))
                .WithExecutionTimeout(TimeSpan.FromMinutes(2));
            runwick.AddJob<RealtimePush>().Named("events.realtime-push").ToQueue("events").WithPriority("high")
                .WithMaxExecutions(3).WithRetryDelay(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10))
                .WithExecutionTimeout(TimeSpan.FromSeconds(15));
            runwick.AddJob<PulseReload>().Named("events.pulse-reload").ToQueue("events").WithPriority("low")
                .WithMaxExecutions(2).WithRetryDelay(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10))
                .WithExecutionTimeout(TimeSpan.FromSeconds(15))
                .UniqueWhileActive(_ => "admins");
            // Cron tasks are not retried; a later occurrence handles transient failures without duplicate reminders.
            runwick.AddJob<ScheduledCronJob>().Named("cron.scheduled").ToQueue("cron").WithPriority("low")
                .WithMaxExecutions(1).WithExecutionTimeout(TimeSpan.FromMinutes(30));

            runwick.AddHandler<SendVerificationEmail, SendVerificationEmailHandler>();
            runwick.AddHandler<OrderPaidEmail, OrderPaidEmailHandler>();
            runwick.AddHandler<OrderShippedEmail, OrderShippedEmailHandler>();
            runwick.AddHandler<OrderCancelledEmail, OrderCancelledEmailHandler>();
            runwick.AddHandler<OrderRefundEmail, OrderRefundEmailHandler>();
            runwick.AddHandler<InstallmentPaidEmail, InstallmentPaidEmailHandler>();
            runwick.AddHandler<InstallmentReminderEmail, InstallmentReminderEmailHandler>();
            runwick.AddHandler<InstallmentStatementEmail, InstallmentStatementEmailHandler>();
            runwick.AddHandler<TicketReplyEmail, TicketReplyEmailHandler>();
            runwick.AddHandler<CareDecisionEmail, CareDecisionEmailHandler>();
            runwick.AddHandler<DeleteAssets, DeleteAssetsHandler>();
            runwick.AddHandler<RealtimePush, RealtimePushHandler>();
            runwick.AddHandler<PulseReload, PulseReloadHandler>();
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
            dashboard.Title = "TechLandier Jobs";
            dashboard.SignOutPath = RunwickManagementAuth.LogoutPath;
        });

        builder.Services.AddHealthChecks().AddRunwick();
        return builder;
    }

    /// <summary>
    /// The demo sleep jobs, their queues and their four pools exist to show Runwick's dashboard under load. They are on
    /// in Development and off elsewhere unless Runwick:Demo:Enabled (RUNWICK_DEMO_ENABLED) says otherwise, so a
    /// production worker does not poll four idle queues and /demo/jobs is not mapped.
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

    /// <summary>Runwick shares the app's database (the <c>runwick</c> schema), so staged jobs commit with business writes.</summary>
    public static string ConnectionString(IConfiguration config) =>
        DatabaseConnection.Parse(config["DATABASE_URL"] ?? config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("DATABASE_URL is required."));

    private static bool IsDashboardAdmin(ClaimsPrincipal principal, string username) =>
        principal.Identity?.IsAuthenticated == true
        && string.Equals(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, username, StringComparison.Ordinal);
}
