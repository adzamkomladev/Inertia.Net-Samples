using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReactSqliteBlog.Infrastructure;
using ReactSqliteBlog.Infrastructure.Setup;
using ReactSqliteBlog.Cron;

namespace ReactSqliteBlog.Tests;

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
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cron:backup"] = "*/2 * * * * *"
        }).Build();
        Assert.Equal("*/2 * * * * *", CronSetup.Expression(config, "backup", "0 3 * * *").Value);
        config["Cron:backup"] = "invalid";
        Assert.Contains("Cron:backup", Assert.Throws<InvalidOperationException>(() => CronSetup.Expression(config, "backup", "0 3 * * *")).Message);
    }
}
