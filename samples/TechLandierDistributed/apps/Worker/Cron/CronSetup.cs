using Runwick;

namespace TechLandier.Infrastructure;

/// Registers every entry in <see cref="CronJobs.Schedule"/> as a Runwick schedule. Materialization is
/// atomic in the Runwick store, so any number of Worker replicas can run the scheduler safely.
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
