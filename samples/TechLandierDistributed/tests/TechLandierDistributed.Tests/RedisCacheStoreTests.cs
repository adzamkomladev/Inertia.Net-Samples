using Inertia.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TechLandier.Caching;
using TechLandier.Infrastructure;

namespace TechLandier.Tests;

/// <summary>Real-Redis store behaviour. Set REDIS_URL to run; otherwise the tests return early.</summary>
public sealed class RedisCacheStoreTests
{
    [Fact]
    public async Task Tag_sets_overwrites_cross_store_visibility_and_ttl()
    {
        using var redis = RedisTestSupport.TryConnect();
        if (redis is null) return;
        var options = Options();
        try
        {
            var store = Store(redis, options);
            var first = new InertiaCacheKey(InertiaCacheEntryKind.Prop, "first");
            var second = new InertiaCacheKey(InertiaCacheEntryKind.Prop, "second");
            var third = new InertiaCacheKey(InertiaCacheEntryKind.Prop, "third");

            await store.SetAsync(first, Bytes("first"), new InertiaCacheEntryOptions(TimeSpan.FromMinutes(5), ["a", "b"]), default);
            await store.SetAsync(second, Bytes("second"), new InertiaCacheEntryOptions(TimeSpan.FromMinutes(5), ["a"]), default);
            await store.SetAsync(third, Bytes("third"), new InertiaCacheEntryOptions(TimeSpan.FromMinutes(5), ["b"]), default);

            // A second store on the same Redis sees the same values.
            var other = Store(redis, options);
            Assert.Equal(Bytes("first"), await other.GetAsync(first, default));

            await other.RemoveByTagAsync("a", default);
            Assert.Null(await store.GetAsync(first, default));
            Assert.Null(await store.GetAsync(second, default));
            Assert.NotNull(await store.GetAsync(third, default));

            // Overwriting with different tags removes the entry from the old tag set.
            await store.SetAsync(third, Bytes("third-new"), new InertiaCacheEntryOptions(TimeSpan.FromMinutes(5), ["c"]), default);
            await store.RemoveByTagAsync("b", default);
            Assert.NotNull(await store.GetAsync(third, default));
            await store.RemoveByTagAsync("c", default);
            Assert.Null(await store.GetAsync(third, default));

            // A newly created tag set has a TTL (the KeyExpire GT trap would leave it infinite).
            await store.SetAsync(first, Bytes("ttl"), new InertiaCacheEntryOptions(TimeSpan.FromMinutes(3), ["ttl"]), default);
            var ttl = await redis.GetDatabase().KeyTimeToLiveAsync(options.Prefix + "tag:ttl");
            Assert.NotNull(ttl);
            Assert.True(ttl > TimeSpan.Zero);
            await store.RemoveByTagAsync("ttl", default);
        }
        finally
        {
            await RedisTestSupport.DeletePrefixAsync(redis, options.Prefix);
        }
    }

    [Fact]
    public async Task Oversized_values_are_not_cached()
    {
        using var redis = RedisTestSupport.TryConnect();
        if (redis is null) return;
        var options = Options(maxEntryBytes: 4);
        try
        {
            var store = Store(redis, options);
            var key = new InertiaCacheKey(InertiaCacheEntryKind.Ssr, "oversized");
            await store.SetAsync(key, Bytes("12345"), new InertiaCacheEntryOptions(TimeSpan.FromMinutes(1), ["ssr"]), default);
            Assert.Null(await store.GetAsync(key, default));
        }
        finally
        {
            await RedisTestSupport.DeletePrefixAsync(redis, options.Prefix);
        }
    }

    [Fact]
    public async Task Unreachable_cache_redis_is_a_miss_and_health_is_degraded()
    {
        var configuration = new StackExchange.Redis.ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectTimeout = 150,
            SyncTimeout = 150,
            AsyncTimeout = 150,
            ConnectRetry = 0,
        };
        configuration.EndPoints.Add("127.0.0.1", 6553);
        using var redis = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(configuration);
        var options = Options();
        var store = Store(redis, options);
        var key = new InertiaCacheKey(InertiaCacheEntryKind.Prop, "offline");

        Assert.Null(await store.GetAsync(key, default));
        await store.SetAsync(key, Bytes("value"), new InertiaCacheEntryOptions(TimeSpan.FromMinutes(1), ["offline"]), default);

        var health = await new CacheRedisHealthCheck(new CacheRedisConnection(redis))
            .CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, health.Status);
    }

    [Fact]
    public void MainApp_registers_the_redis_store_when_cache_redis_is_configured()
    {
        using var redis = RedisTestSupport.TryConnect();
        if (redis is null) return;
        using var app = new RedisCacheFactory();
        Assert.IsType<RedisInertiaCacheStore>(app.Services.GetRequiredService<IInertiaCacheStore>());
    }

    static RedisInertiaCacheStore Store(StackExchange.Redis.IConnectionMultiplexer redis, InertiaCacheStoreOptions options) =>
        new(new CacheRedisConnection(redis), options, NullLogger<RedisInertiaCacheStore>.Instance);

    static InertiaCacheStoreOptions Options(long maxEntryBytes = 1024 * 1024) => new()
    {
        Prefix = $"TechLandierDistributed:test:{Guid.NewGuid():N}:cache:v1:",
        MaxEntryBytes = maxEntryBytes,
    };

    static byte[] Bytes(string value) => System.Text.Encoding.UTF8.GetBytes(value);

    sealed class RedisCacheFactory : AppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Cache:RedisUrl", Environment.GetEnvironmentVariable("REDIS_URL"));
        }
    }
}
