using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

namespace VuePgBlog.Infrastructure;

public static class CronSetup
{
    public static IServiceCollection AddCron(this IServiceCollection services, IConfiguration config)
    {
        if (!config.GetValue("Cron:Enabled", true)) return services;
        var expressions = CronJobs.Schedules(config);
        services.AddScoped<CronJobs>();
        foreach (var entry in CronJobs.Schedule)
        {
            var expression = CronExpression.Parse(config[$"Cron:{entry.Name}"] is { Length: > 0 } value ? value : entry.Cron);
            services.MapTicker(entry.Name, async (_, sp, ct) =>
            {
                if (await sp.GetRequiredService<CronJobs>().Tick(entry, expressions[entry.Name], ct))
                    await sp.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>().AddAsync(new TimeTickerEntity
                    {
                        Function = entry.Name, ExecutionTime = DateTime.UtcNow.AddMinutes(1)
                    });
            }).WithCron(expression).WithMaxConcurrency(1);
        }
        // ponytail: TickerQ definitions are in memory; durable lease rows retain completion and catch-up state.
        services.AddTickerQ(options =>
        {
            options.ConfigureScheduler(s => { s.SchedulerTimeZone = TimeZoneInfo.Utc; s.MaxConcurrency = 1; });
            options.UseTickerSeeder(async (ITimeTickerManager<TimeTickerEntity> manager) =>
            {
                // Establish new baselines and catch up interrupted/missed runs through the same lease guard.
                foreach (var entry in CronJobs.Schedule)
                    await manager.AddAsync(new TimeTickerEntity { Function = entry.Name, ExecutionTime = DateTime.UtcNow });
            });
        });
        return services;
    }
}
