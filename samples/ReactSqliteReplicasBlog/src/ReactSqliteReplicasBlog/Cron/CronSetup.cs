using TickerQ.DependencyInjection;
using TickerQ.Utilities;

namespace ReactSqliteReplicasBlog.Cron;

public sealed record CronTask(string Name, CronExpression Schedule, Func<IServiceProvider, CancellationToken, Task> Run);

public static class CronSetup
{
    public static IServiceCollection AddCron(this IServiceCollection services, IConfiguration config)
    {
        if (!config.GetValue("Cron:Enabled", true)) return services;
        foreach (var task in CronTasks.All)
        {
            var expression = Expression(config, task.Name, task.Schedule.Value);
            services.MapTicker(task.Name, (_, sp, ct) => CronLeaseRunner.RunAsync(task, expression.Value, sp, ct)).WithCron(expression).WithMaxConcurrency(1);
        }
        // ponytail: recurring definitions are reseeded in memory on restart; add a TickerQ store for durable execution history.
        services.AddTickerQ(options => options.ConfigureScheduler(s =>
        {
            s.SchedulerTimeZone = TimeZoneInfo.Utc;
            s.MaxConcurrency = 1;
        }));
        return services;
    }

    internal static CronExpression Expression(IConfiguration config, string name, string fallback)
    {
        var text = config[$"Cron:{name}"] is { Length: > 0 } value ? value : fallback;
        try { return CronExpression.Parse(text); }
        catch (ArgumentException ex) { throw new InvalidOperationException($"Invalid cron expression '{text}' for Cron:{name}.", ex); }
    }
}
