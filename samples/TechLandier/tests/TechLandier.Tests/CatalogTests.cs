using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Features;

namespace TechLandier.Tests;

/// Own fixture: sign-ups share one client IP in tests, and the auth window allows ten per minute.
public sealed class CatalogTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Product_gallery_keeps_order_cover_and_upload_ownership()
    {
        var p = await app.SeedProductAsync("Gallery Seed", price: 20m, stock: 2);
        var (admin, adminId) = await app.AdminAsync("gallery-admin@example.test");
        var (teammate, otherId) = await app.AdminAsync("gallery-other@example.test");
        Asset Image(Guid owner, string name) => new() { OwnerId = owner, Key = $"u/{owner}/{name}.png", ContentType = "image/png", Size = 10, Status = AssetStatus.Ready };
        var (a, b, foreign) = (Image(adminId, "a"), Image(adminId, "b"), Image(otherId, "c"));
        await app.QueryAsync(async db => { db.Assets.AddRange(a, b, foreign); return await db.SaveChangesAsync(); });
        object Body(params Guid[] ids) => new { name = "Gallery Lamp", brand = "Lumen", categoryId = p.CategoryId, price = 40m, status = "active", imageAssetIds = ids };

        // Another admin's unattached upload is refused; nothing is created.
        using (await app.SendAsync(admin, "/admin/catalog", Body(a.Id, foreign.Id))) { }
        Assert.False(await app.QueryAsync(async db => await db.Products.IgnoreQueryFilters().AnyAsync(x => x.Name == "Gallery Lamp")));

        using (await app.SendAsync(admin, "/admin/catalog", Body(a.Id, b.Id))) { }
        var product = await app.QueryAsync(async db => await db.Products.IgnoreQueryFilters().SingleAsync(x => x.Name == "Gallery Lamp"));
        Assert.Equal(a.Id, product.ImageAssetId);
        Assert.Contains($"u/{adminId}/a.png", product.ImageUrl);
        Assert.Equal([a.Id, b.Id], await app.QueryAsync(async db => await db.ProductImages.Where(i => i.ProductId == product.Id).OrderBy(i => i.SortOrder).Select(i => i.AssetId).ToListAsync()));

        // Reorder and drop one: b becomes the cover; a teammate editing keeps photos they didn't upload.
        using (await app.SendAsync(teammate, $"/admin/catalog/{product.Id}", Body(b.Id))) { }
        var edited = await app.QueryAsync(async db => await db.Products.IgnoreQueryFilters().SingleAsync(x => x.Id == product.Id));
        Assert.Equal(b.Id, edited.ImageAssetId);
        Assert.Equal([b.Id], await app.QueryAsync(async db => await db.ProductImages.Where(i => i.ProductId == product.Id).Select(i => i.AssetId).ToListAsync()));

        using var page = await app.InertiaGetAsync(app.Client(), $"/products/{edited.Slug}");
        Assert.Contains($"/u/{adminId}/b.png", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_cover_set_before_galleries_is_shown_and_kept_on_edit()
    {
        var p = await app.SeedProductAsync("Legacy Cover", price: 20m, stock: 2);
        var (admin, _) = await app.AdminAsync("legacy-cover@example.test");
        // Uploaded by someone else (e.g. a former admin) and attached only through ImageAssetId.
        var cover = new Asset { OwnerId = Guid.NewGuid(), Key = "u/legacy/cover.png", ContentType = "image/png", Size = 10, Status = AssetStatus.Ready };
        await app.QueryAsync(async db =>
        {
            db.Assets.Add(cover);
            await db.SaveChangesAsync();
            return await db.Products.IgnoreQueryFilters().Where(x => x.Id == p.ProductId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ImageAssetId, cover.Id));
        });

        using var page = await app.InertiaGetAsync(admin, $"/admin/catalog/{p.ProductId}");
        Assert.Contains(cover.Id.ToString(), await page.Content.ReadAsStringAsync());

        using (await app.SendAsync(admin, $"/admin/catalog/{p.ProductId}", new { name = "Legacy Cover", brand = "Seed", categoryId = p.CategoryId, price = 20m, imageAssetIds = new[] { cover.Id } })) { }
        Assert.Equal(cover.Id, await app.QueryAsync(async db => await db.ProductImages.Where(i => i.ProductId == p.ProductId).Select(i => i.AssetId).SingleAsync()));
    }
}
