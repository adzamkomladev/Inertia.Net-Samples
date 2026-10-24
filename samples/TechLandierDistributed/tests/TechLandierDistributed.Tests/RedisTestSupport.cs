using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using TechLandier.Infrastructure;

namespace TechLandier.Tests;

internal static class RedisTestSupport
{
    /// <summary>Returns null when REDIS_URL is unset or unreachable so Redis integration tests skip cleanly.</summary>
    public static IConnectionMultiplexer? TryConnect()
    {
        var url = Environment.GetEnvironmentVariable("REDIS_URL");
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Redis:Url"] = url })
                .Build();
            var settings = RedisUrlSettings.FromConfiguration(config);
            if (settings is null) return null;
            var multiplexer = ConnectionMultiplexer.Connect(settings.Options);
            multiplexer.GetDatabase().Ping();
            return multiplexer;
        }
        catch
        {
            return null;
        }
    }

    public static async Task DeletePrefixAsync(IConnectionMultiplexer redis, string prefix)
    {
        var db = redis.GetDatabase();
        var result = await db.ExecuteAsync("KEYS", prefix + "*");
        foreach (var key in (RedisKey[])result!)
            await db.KeyDeleteAsync(key);
    }
}
