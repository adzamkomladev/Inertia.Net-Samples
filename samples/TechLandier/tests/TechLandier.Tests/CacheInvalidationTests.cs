using Inertia.Net;
using Microsoft.Extensions.DependencyInjection;
using TechLandier.Features;

namespace TechLandier.Tests;

public sealed class CacheInvalidationTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Creating_a_category_removes_tagged_storefront_props()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<IInertiaCache>();
        var store = scope.ServiceProvider.GetRequiredService<IInertiaCacheStore>();
        var key = new InertiaCacheKey(InertiaCacheEntryKind.Prop, "catalog:probe");

        await cache.SetPropAsync("catalog:probe", new { name = "probe" }, TimeSpan.FromMinutes(1), tags: [StoreCache.Catalog]);
        Assert.NotNull(await store.GetAsync(key, CancellationToken.None));

        var created = await scope.ServiceProvider.GetRequiredService<AdminService>()
            .CreateCategoryAsync(new CategoryInput("Cache probe", "probe", "Package", true), CancellationToken.None);
        Assert.NotNull(created);

        Assert.Null(await store.GetAsync(key, CancellationToken.None));
    }
}
