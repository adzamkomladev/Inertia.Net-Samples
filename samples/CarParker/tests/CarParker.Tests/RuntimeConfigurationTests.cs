using Microsoft.Extensions.Configuration;
using CarParker.Infrastructure;

namespace CarParker.Tests;

public class RuntimeConfigurationTests
{
    [Fact]
    public void Cron_schedule_overrides_accept_seconds_and_report_invalid_setting()
    {
        var name = CronJobs.Schedules(new ConfigurationBuilder().Build()).Keys.First();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"Cron:{name}"] = "*/2 * * * * *"
        }).Build();
        var at = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(at.AddSeconds(2), CronJobs.Schedules(config)[name].GetNextOccurrence(at));
        config[$"Cron:{name}"] = "invalid";
        Assert.Contains($"Cron:{name}", Assert.Throws<InvalidOperationException>(() => CronJobs.Schedules(config)).Message);
    }
}
