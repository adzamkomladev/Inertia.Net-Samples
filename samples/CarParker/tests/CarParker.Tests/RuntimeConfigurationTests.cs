using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CarParker.Infrastructure;
using CarParker.Infrastructure.Setup;

namespace CarParker.Tests;

public class RuntimeConfigurationTests
{
    [Fact]
    public void Search_index_override_is_exact_and_blank_retains_namespaced_default()
    {
        Assert.Equal("custom_lots", new OptionalServiceNamespace("sample", "production", "custom_lots").MeilisearchIndex("lots"));
        Assert.Equal("sample-production-lots", new OptionalServiceNamespace("sample", "production", "").MeilisearchIndex("lots"));
        Assert.Throws<ArgumentException>(() => new OptionalServiceNamespace("sample", "production", "invalid/index"));
    }

    [Fact]
    public void Feature_registration_reads_index_from_configuration()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MEILISEARCH_INDEX"] = "configured_lots", ["Cron:Enabled"] = "false"
        });
        builder.AddBlogFeatures();
        using var services = builder.Services.BuildServiceProvider();
        Assert.Equal("configured_lots", services.GetRequiredService<OptionalServiceNamespace>().MeilisearchIndex("lots"));
    }

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
