using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TechLandier.Infrastructure;

namespace TechLandier.Tests;

/// <summary>Low-stock dedupe must be shared by replicas; set REDIS_URL to run the Redis gate test.</summary>
public sealed class StockAlertGateTests
{
    [Fact]
    public async Task Redis_gate_fires_once_and_resets_above_the_threshold()
    {
        using var redis = RedisTestSupport.TryConnect();
        if (redis is null) return;

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Replicas:Namespace"] = $"TechLandierDistributed:test:{Guid.NewGuid():N}",
        }).Build();
        var productId = Guid.NewGuid();
        var first = new StockAlerts(config, NullLogger<StockAlerts>.Instance, redis);
        var second = new StockAlerts(config, NullLogger<StockAlerts>.Instance, redis);

        Assert.True(await first.ShouldAlertAsync(productId, 2, 5));
        Assert.False(await second.ShouldAlertAsync(productId, 1, 5)); // another replica sees the gate

        Assert.False(await second.ShouldAlertAsync(productId, 6, 5)); // restock clears it
        Assert.True(await first.ShouldAlertAsync(productId, 2, 5));   // next dip fires again

        await RedisTestSupport.DeletePrefixAsync(redis, config["Replicas:Namespace"] + ":stock-alert:");
    }

    [Fact]
    public async Task In_memory_gate_keeps_the_single_process_contract()
    {
        var config = new ConfigurationBuilder().Build();
        var alerts = new StockAlerts(config, NullLogger<StockAlerts>.Instance);
        var productId = Guid.NewGuid();

        Assert.True(await alerts.ShouldAlertAsync(productId, 2, 5));
        Assert.False(await alerts.ShouldAlertAsync(productId, 1, 5));
        Assert.False(await alerts.ShouldAlertAsync(productId, 5, 5));
        Assert.True(await alerts.ShouldAlertAsync(productId, 4, 5));
    }
}
