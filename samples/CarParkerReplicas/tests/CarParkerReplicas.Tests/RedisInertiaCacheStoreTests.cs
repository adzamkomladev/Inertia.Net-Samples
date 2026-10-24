using CarParker.Infrastructure.Caching;
using CarParker.Infrastructure.Setup;
using Inertia.Net;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit.Abstractions;

namespace CarParker.Tests;

/// <summary>
/// Exercises the Redis store against a real server when <c>REDIS_URL</c> is set and reachable.
/// The default suite deliberately does not require Docker: without the variable each test returns
/// after writing a skip reason to the test output.
/// </summary>
public sealed class RedisInertiaCacheStoreTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Tag_removal_is_shared_between_store_instances_and_only_removes_matching_entries()
    {
        var connection = await ConnectAsync();
        if (connection is null) return;
        using var redis = connection.Value.Redis;
        var options = connection.Value.Options;
        var store = Store(redis, options);
        var other = Store(redis, options);
        var first = Key("first");
        var second = Key("second");

        await store.SetAsync(first, [1, 2, 3], new InertiaCacheEntryOptions(
            TimeSpan.FromMinutes(5), ["alpha", "shared"]), CancellationToken.None);
        await store.SetAsync(second, [4], new InertiaCacheEntryOptions(
            TimeSpan.FromMinutes(5), ["beta", "shared"]), CancellationToken.None);

        Assert.NotNull(await other.GetAsync(first, CancellationToken.None));
        await other.RemoveByTagAsync("alpha", CancellationToken.None);
        Assert.Null(await store.GetAsync(first, CancellationToken.None));
        Assert.NotNull(await store.GetAsync(second, CancellationToken.None));

        await store.RemoveByTagAsync("shared", CancellationToken.None);
        Assert.Null(await other.GetAsync(second, CancellationToken.None));
    }

    [Fact]
    public async Task Overwriting_with_different_tags_keeps_the_new_entry_when_the_old_tag_is_removed()
    {
        var connection = await ConnectAsync();
        if (connection is null) return;
        using var redis = connection.Value.Redis;
        var options = connection.Value.Options;
        var store = Store(redis, options);
        var key = Key("overwrite");

        await store.SetAsync(key, [1], new InertiaCacheEntryOptions(
            TimeSpan.FromMinutes(5), ["old", "keep"]), CancellationToken.None);
        await store.SetAsync(key, [2], new InertiaCacheEntryOptions(
            TimeSpan.FromMinutes(5), ["keep"]), CancellationToken.None);

        await store.RemoveByTagAsync("old", CancellationToken.None);
        Assert.Equal([2], await store.GetAsync(key, CancellationToken.None));

        await store.RemoveByTagAsync("keep", CancellationToken.None);
        Assert.Null(await store.GetAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task New_tag_sets_get_a_ttl_and_a_longer_entry_extends_it()
    {
        var connection = await ConnectAsync();
        if (connection is null) return;
        using var redis = connection.Value.Redis;
        var options = connection.Value.Options;
        var store = Store(redis, options);
        var database = redis.GetDatabase();
        var tagKey = RedisCacheKeys.Tag(options.Prefix, "ttl");

        await store.SetAsync(Key("short"), [1], new InertiaCacheEntryOptions(
            TimeSpan.FromSeconds(10), ["ttl"]), CancellationToken.None);
        var shortTtl = await database.KeyTimeToLiveAsync(tagKey);
        Assert.NotNull(shortTtl);
        Assert.True(shortTtl > TimeSpan.Zero, "a new tag set must receive a TTL (the ExpireWhen.GT trap)");

        await store.SetAsync(Key("long"), [2], new InertiaCacheEntryOptions(
            TimeSpan.FromMinutes(5), ["ttl"]), CancellationToken.None);
        var extended = await database.KeyTimeToLiveAsync(tagKey);
        Assert.NotNull(extended);
        Assert.True(extended > shortTtl, "a longer entry must extend the tag set TTL");
        Assert.True(extended >= TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Max_entry_bytes_skips_oversized_values_and_keeps_normal_values()
    {
        var connection = await ConnectAsync();
        if (connection is null) return;
        using var redis = connection.Value.Redis;
        var options = connection.Value.Options with { MaxEntryBytes = 2 };
        var store = Store(redis, options);
        var large = Key("large");

        await store.SetAsync(large, [1, 2, 3], new InertiaCacheEntryOptions(
            TimeSpan.FromMinutes(5), ["size"]), CancellationToken.None);
        Assert.Null(await store.GetAsync(large, CancellationToken.None));

        var small = Key("small");
        await store.SetAsync(small, [1, 2], new InertiaCacheEntryOptions(
            TimeSpan.FromMinutes(5), ["size"]), CancellationToken.None);
        Assert.Equal([1, 2], await store.GetAsync(small, CancellationToken.None));

        // An oversized replacement must not leave the smaller, older value behind.
        await store.SetAsync(small, [1, 2, 3], new InertiaCacheEntryOptions(
            TimeSpan.FromMinutes(5), ["size"]), CancellationToken.None);
        Assert.Null(await store.GetAsync(small, CancellationToken.None));
    }

    static RedisInertiaCacheStore Store(IConnectionMultiplexer redis, RedisInertiaCacheOptions options) =>
        new(redis, options, NullLogger<RedisInertiaCacheStore>.Instance);

    static InertiaCacheKey Key(string value) =>
        new(InertiaCacheEntryKind.Prop, value + ":" + Guid.NewGuid().ToString("N"));

    async Task<(ConnectionMultiplexer Redis, RedisInertiaCacheOptions Options)?> ConnectAsync()
    {
        var url = Environment.GetEnvironmentVariable("REDIS_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            output.WriteLine("Skipping Redis cache integration test: REDIS_URL is not set.");
            return null;
        }

        RedisUrlSettings parsed;
        try
        {
            parsed = RedisUrlSettings.Parse(url);
        }
        catch (Exception exception)
        {
            output.WriteLine($"Skipping Redis cache integration test: invalid REDIS_URL ({exception.Message}).");
            return null;
        }

        var configuration = parsed.ToConfigurationOptions();
        configuration.AbortOnConnectFail = false;
        configuration.ConnectTimeout = 2000;
        configuration.SyncTimeout = 2000;
        configuration.AsyncTimeout = 2000;
        configuration.ConnectRetry = 1;
        var redis = await ConnectionMultiplexer.ConnectAsync(configuration);
        try
        {
            await redis.GetDatabase().PingAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception exception)
        {
            output.WriteLine($"Skipping Redis cache integration test: {exception.GetType().Name}.");
            await redis.CloseAsync();
            redis.Dispose();
            return null;
        }

        var options = new RedisInertiaCacheOptions(
            RedisCacheKeys.Prefix("CarParkerReplicas:test:" + Guid.NewGuid().ToString("N"), "v1"),
            "v1", 0, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
        return (redis, options);
    }
}
