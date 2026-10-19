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
    [InlineData("https://blog.example.com/", "https://blog.example.com")]
    [InlineData("http://localhost:8183", "http://localhost:8183")]
    public void Public_urls_use_the_configured_gateway(string value, string expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = value }).Build();
        Assert.Equal(expected, config.RequiredAppBaseUrl());
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Host = new Microsoft.AspNetCore.Http.HostString("private-replica", 5213);
        var protection = new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider();
        var user = new VuePgReplicasAdvBlog.Data.User { Id = Guid.NewGuid(), Email = "demo@example.test", Name = "Demo" };
        var url = main::VuePgReplicasAdvBlog.MainApp.Infrastructure.AuthHttp.VerificationUrl(context, config, protection, user);
        Assert.StartsWith(expected + "/email/verify/confirm?token=", url);
        Assert.DoesNotContain("private-replica", url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/posts")]
    [InlineData("ftp://blog.example.com")]
    [InlineData("https://user:password@blog.example.com")]
    [InlineData("https://blog.example.com?redirect=replica")]
    public void Missing_or_invalid_public_urls_cannot_fall_back_to_a_replica(string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = value }).Build();
        Assert.Throws<InvalidOperationException>(() => config.RequiredAppBaseUrl());
    }

    [Fact]
    public void Ticker_entities_round_trip_with_children_and_execution_metadata()
    {
        var entity = new TickerQ.Utilities.Entities.TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = "test",
            ExecutionTime = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc),
            Children = [new() { Id = Guid.NewGuid(), Function = "child", ExecutionTime = DateTime.UtcNow }]
        };
        var options = new System.Text.Json.JsonSerializerOptions { TypeInfoResolver = CronJsonContext.Default };
        var json = System.Text.Json.JsonSerializer.Serialize(entity, options);
        var restored = System.Text.Json.JsonSerializer.Deserialize<TickerQ.Utilities.Entities.TimeTickerEntity>(json, options)!;
        Assert.Equal(entity.Id, restored.Id);
        Assert.Equal(entity.Children.Single().Id, restored.Children.Single().Id);
        Assert.Equal(entity.ExecutionTime, restored.ExecutionTime);
        var payload = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        payload["UpdatedAt"] = "2026-10-12T12:00:00.0000000Z";
        restored = System.Text.Json.JsonSerializer.Deserialize<TickerQ.Utilities.Entities.TimeTickerEntity>(payload.ToJsonString(), options)!;
        Assert.Contains("2026-10-12T12:00:00.0000000Z", System.Text.Json.JsonSerializer.Serialize(restored, options));
        entity.Children = [];
        json = System.Text.Json.JsonSerializer.Serialize(entity, options).Replace("\"Children\":[]", "\"Children\":{}");
        Assert.Empty(System.Text.Json.JsonSerializer.Deserialize<TickerQ.Utilities.Entities.TimeTickerEntity>(json, options)!.Children);
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<TickerQ.Utilities.Entities.TimeTickerEntity>(
            json.Replace("\"Children\":{}", "\"Children\":{\"unexpected\":1}"), options));
    }

    [Fact]
    public void Demo_seed_counts_are_configurable_and_validated()
    {
        var config = new ConfigurationManager();
        Assert.Equal(new main::VuePgReplicasAdvBlog.MainApp.Data.DemoSeedSize(200, 1000),
            main::VuePgReplicasAdvBlog.MainApp.Setup.Cli.DemoSize(config));
        config["Seed:Users"] = "20";
        config["Seed:Posts"] = "40";
        Assert.Equal(new main::VuePgReplicasAdvBlog.MainApp.Data.DemoSeedSize(20, 40),
            main::VuePgReplicasAdvBlog.MainApp.Setup.Cli.DemoSize(config));
        config["Seed:Users"] = "0";
        Assert.Throws<InvalidOperationException>(() => main::VuePgReplicasAdvBlog.MainApp.Setup.Cli.DemoSize(config));
    }

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
        Assert.NotNull(ssr.NodeProcess);
        // 1.3: the managed process takes its port from NodeProcess.BasePort (Ssr.Url was removed).
        Assert.Equal(expectedPort, ssr.NodeProcess!.BasePort);
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
