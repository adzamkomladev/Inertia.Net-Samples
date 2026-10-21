using Microsoft.Extensions.Configuration;
using Inkwell.Cron;

namespace Inkwell.Tests;

public class RuntimeConfigurationTests
{
    [Fact]
    public void Cron_schedule_overrides_accept_seconds_and_report_invalid_setting()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cron:backup"] = "*/2 * * * * *"
        }).Build();
        Assert.Equal("*/2 * * * * *", CronSetup.Expression(config, "backup", "0 3 * * *").Value);
        config["Cron:backup"] = "invalid";
        Assert.Contains("Cron:backup", Assert.Throws<InvalidOperationException>(() => CronSetup.Expression(config, "backup", "0 3 * * *")).Message);
    }
}
