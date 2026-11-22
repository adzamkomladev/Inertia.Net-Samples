using Npgsql;
using Runwick;

namespace TechLandier.Infrastructure;

// Shared job contracts between the MainApp producer and the Worker consumer. The application jobs (mail, events, cron)
// are in Jobs/Jobs.cs; the demo sleep payloads below exist only to show the dashboard under load.
public sealed record DemoSleep100ms(Guid BatchId, int Index, DateTimeOffset EnqueuedAt) : IQueueJob;
public sealed record DemoSleep500ms(Guid BatchId, int Index, DateTimeOffset EnqueuedAt) : IQueueJob;
public sealed record DemoSleep2s(Guid BatchId, int Index, DateTimeOffset EnqueuedAt) : IQueueJob;
public sealed record DemoSleep10s(Guid BatchId, int Index, DateTimeOffset EnqueuedAt) : IQueueJob;
public sealed record DemoSleep30s(Guid BatchId, int Index, DateTimeOffset EnqueuedAt) : IQueueJob;

public static class RunwickDefaults
{
    public const string ApplicationName = "techlandier";

    /// <summary>
    /// The demo sleep jobs, their queues and their four worker pools exist to show Runwick's dashboard under load. They are
    /// on in Development and off elsewhere unless Runwick:Demo:Enabled (RUNWICK_DEMO_ENABLED) says otherwise, so a
    /// production worker does not poll four idle queues and /demo/jobs is not mapped.
    /// </summary>
    public static bool DemoEnabled(IConfiguration config, IHostEnvironment environment) =>
        config.GetValue("Runwick:Demo:Enabled", environment.IsDevelopment());

    /// <summary>
    /// Runwick lives in the application's database (the <c>runwick</c> schema), so jobs staged through
    /// <see cref="IJobOutbox"/> commit with the business rows. RUNWICK_DATABASE_URL must reach that same primary database.
    /// </summary>
    public static string ConnectionString(IConfiguration config)
    {
        var value = config["Runwick:DatabaseUrl"]
            ?? throw new InvalidOperationException("RUNWICK_DATABASE_URL must point at the PostgreSQL primary.");
        return Parse(value);
    }

    /// <summary>Declares the queues and contracts shared by producers and workers. Handlers/workers are host-specific.</summary>
    public static RunwickBuilder AddRunwickJobs(this RunwickBuilder runwick, bool demoEnabled)
    {
        runwick.Queue("cron", q => q.MaxConcurrent(1))
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

        if (demoEnabled) AddDemoJobs(runwick);
        return runwick;
    }

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
        return connection.ConnectionString;
    }
}
