extern alias main;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VuePgReplicasAdvBlog.Infrastructure;
using VuePgReplicasAdvBlog.Infrastructure.Setup;

namespace VuePgReplicasAdvBlog.Tests;

public class RuntimeConfigurationTests
{
    [Theory]
    [InlineData(null, "false")]
    [InlineData("true", "true")]
    public void Application_aliases_preserve_explicit_host_settings(string? canonical, string expected)
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?> { ["SSR_ENABLED"] = "false", ["Inertia:Ssr"] = canonical });
        config.ApplyPlainNames(("SSR_ENABLED", "Inertia:Ssr"));
        Assert.Equal(expected, config["Inertia:Ssr"]);
    }

    [Theory]
    [InlineData(null, 13714)]
    [InlineData("23145", 23145)]
    public void Ssr_client_uses_the_configured_process_port(string? configuredPort, int expectedPort)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Inertia:SsrPort"] = configuredPort });
        main::VuePgReplicasAdvBlog.MainApp.Setup.InertiaSetup.AddMainAppInertia(builder);
        using var services = builder.Services.BuildServiceProvider();
        var ssr = services.GetRequiredService<IOptions<InertiaOptions>>().Value.Ssr;
        Assert.Equal($"http://127.0.0.1:{expectedPort}", ssr.Url);
        Assert.NotNull(ssr.NodeProcess);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    public void Ssr_rejects_an_invalid_process_port(string port)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Inertia:SsrPort"] = port });
        main::VuePgReplicasAdvBlog.MainApp.Setup.InertiaSetup.AddMainAppInertia(builder);
        using var services = builder.Services.BuildServiceProvider();
        Assert.Contains("Inertia:SsrPort", Assert.Throws<InvalidOperationException>(() =>
            services.GetRequiredService<IOptions<InertiaOptions>>().Value).Message);
    }

    [Fact]
    public void Aspire_redis_connection_takes_precedence_over_the_local_env_url()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:redis"] = "localhost:1234,user=worker,password=test,defaultDatabase=2,ssl=true",
            ["REDIS_URL"] = "redis://obsolete:6379"
        }).Build();
        var redis = RedisUrlSettings.FromConfiguration(config)!;
        Assert.Equal("localhost", redis.Host);
        Assert.Equal(1234, redis.Port);
        Assert.Equal(2, redis.Database);
        Assert.True(redis.UseTls);
        Assert.Equal("worker", redis.Username);
        Assert.Equal("test", redis.Password);
    }

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
            ["MEILISEARCH_INDEX"] = "configured_posts",
            ["Cron:Enabled"] = "false",
            ["Replicas:Enabled"] = "false"
        });
        builder.AddCommonInfrastructure();
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
