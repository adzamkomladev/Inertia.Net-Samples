using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VuePgBlog.Infrastructure;
using VuePgBlog.Infrastructure.Setup;

namespace VuePgBlog.Tests;

public class RuntimeConfigurationTests
{
    [Fact]
    public void Search_index_override_is_exact_and_blank_retains_namespaced_default()
    {
        Assert.Equal("custom_posts", new OptionalServiceNamespace("sample", "production", "custom_posts").MeilisearchIndex("posts"));
        Assert.Equal("sample-production-posts", new OptionalServiceNamespace("sample", "production", "").MeilisearchIndex("posts"));
        Assert.Throws<ArgumentException>(() => new OptionalServiceNamespace("sample", "production", "invalid/index"));
    }

    [Fact]
    public void Feature_registration_reads_index_from_configuration()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MEILISEARCH_INDEX"] = "configured_posts", ["Cron:Enabled"] = "false"
        });
        builder.AddBlogFeatures();
        using var services = builder.Services.BuildServiceProvider();
        Assert.Equal("configured_posts", services.GetRequiredService<OptionalServiceNamespace>().MeilisearchIndex("posts"));
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
