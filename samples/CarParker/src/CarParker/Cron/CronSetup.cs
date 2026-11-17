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

    /// <summary>
    /// Runwick 0.1.3 gives each schedule a fixed 250 ms wall-clock budget when it seeds them at startup. On a cold
    /// process (Debug build, small VPS, several containers booting together) the first evaluation pays for loading
    /// the NodaTime time-zone database and JIT-compiling Cronos, which can exceed that budget and crash the host.
    /// Doing that one-time work here, before the host starts, keeps the seeding well inside the budget.
    /// </summary>
    public static void WarmScheduleEvaluation(IConfiguration config)
    {
        var zone = NodaTime.DateTimeZoneProviders.Tzdb["Etc/UTC"];
        var now = NodaTime.SystemClock.Instance.GetCurrentInstant();
        _ = now.InZone(zone).LocalDateTime.PlusDays(1).InZoneLeniently(zone);
        foreach (var entry in CronJobs.Schedule)
        {
            var expression = CronJobs.Expression(config, entry.Name);
            var format = CronJobs.Format(expression) == ScheduleCronFormat.SixField ? Cronos.CronFormat.IncludeSeconds : Cronos.CronFormat.Standard;
            _ = Cronos.CronExpression.Parse(expression, format).GetNextOccurrence(DateTime.UtcNow);
        }
    }
}
