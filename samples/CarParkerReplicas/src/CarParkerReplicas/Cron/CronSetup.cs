using Runwick;

namespace CarParker.Infrastructure;

/// Registers every entry in <see cref="CronJobs.Schedule"/> as a Runwick schedule. Runwick materializes
/// due occurrences into durable `cron.scheduled` jobs; the `cron-fixed` pool runs them one at a time.
public static class CronSetup
{
    public static RunwickBuilder AddCronSchedules(this RunwickBuilder runwick, IConfiguration config)
    {
        foreach (var entry in CronJobs.Schedule)
        {
            var expression = CronJobs.Expression(config, entry.Name);
            runwick.AddSchedule(entry.Name, new ScheduledCronJob(entry.Name))
                .Cron(expression, CronJobs.Format(expression))
                .OnMisfire(MisfirePolicy.RunOnce, TimeSpan.FromMinutes(10))
                .Overlap(ScheduleOverlap.Serialize)
                .WithOptions(new DispatchOptions { Queue = "cron", Priority = "low" });
        }
        return runwick;
    }
}
