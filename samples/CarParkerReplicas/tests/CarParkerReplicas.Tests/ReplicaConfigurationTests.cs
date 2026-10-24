using CarParker.Infrastructure.Caching;
using CarParker.Infrastructure.Setup;
using Microsoft.Extensions.Configuration;

namespace CarParker.Tests;

public class ReplicaConfigurationTests
{
    [Fact]
    public void Redis_url_parses_credentials_database_and_tls_without_leaking_them_in_text()
    {
        var settings = RedisUrlSettings.Parse("rediss://cache-user:p%40ssword@redis.example.com:6380/2?ssl=true");

        Assert.Equal("redis.example.com", settings.Host);
        Assert.Equal(6380, settings.Port);
        Assert.Equal(2, settings.Database);
        Assert.True(settings.UseTls);
        Assert.Equal("cache-user", settings.Username);
        Assert.Equal("p@ssword", settings.Password);
        Assert.DoesNotContain("p@ssword", settings.ToString());
        Assert.Contains("credentials redacted", settings.ToString());
    }

    [Fact]
    public void Redis_url_defaults_port_and_supports_password_only_userinfo()
    {
        var settings = RedisUrlSettings.Parse("redis://:secret@localhost/3");

        Assert.Equal(6379, settings.Port);
        Assert.Equal(3, settings.Database);
        Assert.False(settings.UseTls);
        Assert.Null(settings.Username);
        Assert.Equal("secret", settings.Password);
    }

    [Theory]
    [InlineData("http://localhost:6379")]
    [InlineData("redis://localhost:6379/not-a-number")]
    [InlineData("redis://localhost:6379/-1")]
    [InlineData("redis://localhost:6379?timeout=1")]
    [InlineData("redis://localhost:6379#fragment")]
    public void Invalid_redis_urls_fail_clearly(string value) =>
        Assert.ThrowsAny<Exception>(() => RedisUrlSettings.Parse(value));

    [Fact]
    public void Namespace_is_sample_specific_and_configurable()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Replicas:Namespace"] = "cpr:staging",
        }).Build();

        Assert.Equal("cpr:staging", ReplicaSetup.Namespace(config, "Production"));
        Assert.Equal("CarParkerReplicas:staging", ReplicaSetup.Namespace(
            new ConfigurationBuilder().Build(), "Staging"));
    }

    [Fact]
    public void Replicas_enabled_requires_redis_but_the_initializer_may_disable_replicas()
    {
        var enabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Replicas:Enabled"] = "true",
        }).Build();
        var disabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Replicas:Enabled"] = "false",
        }).Build();
        var withRedis = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Replicas:Enabled"] = "true",
            ["REDIS_URL"] = "redis://localhost:6388/0",
        }).Build();

        var error = Assert.Throws<InvalidOperationException>(() => ReplicaSetup.Validate(enabled));
        Assert.Contains("REDIS_URL", error.Message);
        Assert.Null(ReplicaSetup.Validate(disabled));
        Assert.Equal(6388, ReplicaSetup.Validate(withRedis)!.Port);
        Assert.True(ReplicaSetup.Enabled(enabled));
        Assert.False(ReplicaSetup.Enabled(disabled));
    }

    [Fact]
    public void Cache_version_prefers_the_canonical_setting_and_defaults_to_v1()
    {
        var defaultConfig = new ConfigurationBuilder().Build();
        Assert.Equal("v1", RedisInertiaCacheOptions.VersionFrom(defaultConfig));

        var plain = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CACHE_VERSION"] = "release-42",
        }).Build();
        Assert.Equal("release-42", RedisInertiaCacheOptions.VersionFrom(plain));

        var canonical = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Version"] = "release-43",
            ["CACHE_VERSION"] = "release-42",
        }).Build();
        Assert.Equal("release-43", RedisInertiaCacheOptions.VersionFrom(canonical));
    }

    [Fact]
    public void Cache_options_build_the_namespaced_versioned_prefix_and_validate_limits()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Version"] = "2026.10",
            ["Cache:MaxEntryBytes"] = "1048576",
        }).Build();

        var options = RedisInertiaCacheOptions.FromConfiguration(config, "CarParkerReplicas:production");

        Assert.Equal("CarParkerReplicas:production:cache:2026.10:", options.Prefix);
        Assert.Equal("2026.10", options.Version);
        Assert.Equal(1_048_576, options.MaxEntryBytes);
        Assert.Equal(TimeSpan.FromSeconds(60), options.TagTtlPadding);

        var negative = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:MaxEntryBytes"] = "-1",
        }).Build();
        Assert.Throws<InvalidOperationException>(() =>
            RedisInertiaCacheOptions.FromConfiguration(negative, "ns"));
    }

    [Fact]
    public void Cache_keys_use_the_inertia_flat_form()
    {
        var prefix = RedisCacheKeys.Prefix("CarParkerReplicas:test", "v1");
        var prop = new Inertia.Net.InertiaCacheKey(Inertia.Net.InertiaCacheEntryKind.Prop, "notifications:42");
        var ssr = new Inertia.Net.InertiaCacheKey(Inertia.Net.InertiaCacheEntryKind.Ssr, "abc123");
        var tag = RedisCacheKeys.Tag(prefix, "analytics");

        Assert.Equal("CarParkerReplicas:test:cache:v1:inertia:prop:notifications:42", RedisCacheKeys.Entry(prefix, prop));
        Assert.Equal("CarParkerReplicas:test:cache:v1:inertia:ssr:abc123", RedisCacheKeys.Entry(prefix, ssr));
        Assert.Equal("CarParkerReplicas:test:cache:v1:tag:analytics", tag);
        Assert.StartsWith(prefix + "tags:", RedisCacheKeys.Tags(prefix, RedisCacheKeys.Entry(prefix, prop)));
    }

    [Fact]
    public void Initialize_on_startup_defaults_to_true_but_cli_commands_always_initialize()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        Assert.True(Cli.ShouldInitialize(config, []));
        Assert.True(Cli.ShouldInitialize(config, ["--initialize"]));
        Assert.True(Cli.ShouldInitialize(config, ["--seed-demo"]));
        Assert.True(Cli.ShouldInitialize(config, ["--create-admin"]));

        config["Database:InitializeOnStartup"] = "false";
        Assert.False(Cli.ShouldInitialize(config, []));
        Assert.True(Cli.ShouldInitialize(config, ["--initialize"]));
        Assert.True(Cli.ShouldInitialize(config, ["--seed-demo"]));
        Assert.True(Cli.ShouldInitialize(config, ["--create-admin"]));
    }
}
