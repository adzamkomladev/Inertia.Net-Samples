using Npgsql;
using Runwick;

namespace TechLandier.Infrastructure;

// Shared job contracts between the MainApp producer and the Worker consumer.
public sealed record DemoSleep100ms(Guid BatchId, int Index, DateTimeOffset EnqueuedAt) : IQueueJob;
public sealed record DemoSleep500ms(Guid BatchId, int Index, DateTimeOffset EnqueuedAt) : IQueueJob;
public sealed record DemoSleep2s(Guid BatchId, int Index, DateTimeOffset EnqueuedAt) : IQueueJob;
public sealed record DemoSleep10s(Guid BatchId, int Index, DateTimeOffset EnqueuedAt) : IQueueJob;
public sealed record DemoSleep30s(Guid BatchId, int Index, DateTimeOffset EnqueuedAt) : IQueueJob;
public sealed record ScheduledCronJob(string Name) : IQueueJob;

public static class RunwickDefaults
{
    public const string ApplicationName = "techlandier";

    public static bool IsEnabled(IConfiguration config) => config.GetValue("Runwick:Enabled", true);

    public static string ConnectionString(IConfiguration config)
    {
        var value = config["Runwick:DatabaseUrl"]
            ?? throw new InvalidOperationException("RUNWICK_DATABASE_URL must point at the PostgreSQL primary when Runwick is enabled.");
        return Parse(value);
    }

    /// <summary>Declares the queues and contracts shared by producers and workers. Handlers/workers are host-specific.</summary>
    public static RunwickBuilder AddRunwickJobs(this RunwickBuilder runwick)
    {
        runwick.Queue("cron", q => q.MaxConcurrent(1))
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
        return runwick;
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
