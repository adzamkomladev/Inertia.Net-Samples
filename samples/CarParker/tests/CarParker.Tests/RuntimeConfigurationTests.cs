using Microsoft.Extensions.Configuration;
using CarParker.Infrastructure;

namespace CarParker.Tests;

public class RuntimeConfigurationTests
{
    [Fact]
    public void Cron_schedule_overrides_accept_seconds_and_report_invalid_setting()
    {
        var name = CronJobs.Schedule.First().Name;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"Cron:{name}"] = "*/2 * * * * *"
        }).Build();
        Assert.Equal("*/2 * * * * *", CronJobs.Expression(config, name));
        Assert.Equal(Runwick.ScheduleCronFormat.SixField, CronJobs.Format("*/2 * * * * *"));

        config[$"Cron:{name}"] = "invalid";
        var error = Assert.Throws<InvalidOperationException>(() => CronJobs.Expression(config, name));
        Assert.Contains($"Cron:{name}", error.Message);
    }
}
