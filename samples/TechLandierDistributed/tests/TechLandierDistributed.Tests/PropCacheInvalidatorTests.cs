using Inertia.Net;
using Microsoft.Extensions.DependencyInjection;
using TechLandier.Caching;
using TechLandier.Features;
using TechLandier.Infrastructure;

namespace TechLandier.Tests;

/// <summary>The two invalidator adapters keep Core free of Inertia.Net.</summary>
public sealed class PropCacheInvalidatorTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task MainApp_adapter_invalidates_through_the_inertia_cache()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<IInertiaCache>();
        var store = scope.ServiceProvider.GetRequiredService<IInertiaCacheStore>();
        var invalidator = scope.ServiceProvider.GetRequiredService<IPropCacheInvalidator>();
        Assert.IsType<InertiaPropCacheInvalidator>(invalidator);

        await cache.SetPropAsync("adapter:probe", new { name = "probe" }, TimeSpan.FromMinutes(1), tags: ["adapter"]);
        var key = new InertiaCacheKey(InertiaCacheEntryKind.Prop, "adapter:probe");
        Assert.NotNull(await store.GetAsync(key, CancellationToken.None));

        await invalidator.RemoveByTagAsync("adapter");
        Assert.Null(await store.GetAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task Worker_adapter_invalidates_the_shared_store_directly()
    {
        var store = new RecordingStore();
        var invalidator = new RedisPropCacheInvalidator(store);
        await invalidator.RemoveByTagAsync("catalog");
        Assert.Equal("catalog", store.RemovedTag);
    }

    sealed class RecordingStore : IInertiaCacheStore
    {
        public string? RemovedTag { get; private set; }

        public async ValueTask<byte[]?> GetAsync(InertiaCacheKey key, CancellationToken ct)
        {
            await Task.CompletedTask;
            return null;
        }

        public async ValueTask SetAsync(InertiaCacheKey key, byte[] value, InertiaCacheEntryOptions options, CancellationToken ct) =>
            await Task.CompletedTask;

        public async ValueTask RemoveAsync(InertiaCacheKey key, CancellationToken ct) =>
            await Task.CompletedTask;

        public async ValueTask RemoveByTagAsync(string tag, CancellationToken ct)
        {
            RemovedTag = tag;
            await Task.CompletedTask;
        }
    }
}
