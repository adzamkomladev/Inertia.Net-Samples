using StackExchange.Redis;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

namespace VuePgReplicasAdvBlog.Infrastructure;

public static class CronSetup
{
    public static IServiceCollection AddCron(this IServiceCollection services, IConfiguration config)
    {
        if (!config.GetValue("Cron:Enabled", true)) return services;
        var redis = RedisUrlSettings.FromConfiguration(config)
            ?? throw new InvalidOperationException("Redis is required for TickerQ coordination.");
        var database = config.GetValue("TickerQ:RedisDatabase", 1);
        if (database < 0 || database == redis.Database)
            throw new InvalidOperationException("TickerQ:RedisDatabase must be a separate, non-negative Redis database.");
        var redisOptions = new ConfigurationOptions
        {
            User = redis.Username,
            Password = redis.Password,
            Ssl = redis.UseTls,
            DefaultDatabase = database,
            AbortOnConnectFail = false
        };
        redisOptions.EndPoints.Add(redis.Host, redis.Port);
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
                        Function = entry.Name,
                        ExecutionTime = DateTime.UtcNow.AddMinutes(1)
                    });
            }).WithCron(expression).WithMaxConcurrency(1);
        }
        services.AddTickerQ(options =>
        {
            options.AddStackExchangeRedis(r =>
            {
                r.ConfigurationOptions = redisOptions;
                r.InstanceName = (config["Replicas:Namespace"] ?? "VuePgReplicasAdvBlog") + ":tickerq:";
                // TickerQ 10.4.0's default context serializes Children incorrectly; use explicit entity metadata.
                r.JsonSerializerContext = CronJsonContext.Default;
            });
            options.ConfigureScheduler(s =>
            {
                s.SchedulerTimeZone = TimeZoneInfo.Utc;
                s.MaxConcurrency = 1;
                s.NodeIdentifier = config["INSTANCE_ID"] ?? $"{Environment.MachineName}:{Environment.ProcessId}";
            });
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
