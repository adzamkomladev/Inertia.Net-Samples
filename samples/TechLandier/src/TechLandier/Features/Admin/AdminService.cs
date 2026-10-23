using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;
using Inertia.Net;

namespace TechLandier.Features;

/// Admin dashboard numbers and catalog management. The dashboard loaders run as parallel Inertia props,
/// so they use their own contexts; everything else uses the request's.
public sealed class AdminService(AppDb db, IDbContextFactory<AppDb> dbs, RealtimeEvents realtime, IInertiaCache cache, Storage storage)
{
    const int CatalogPageSize = 20;

    // ---------------------------------------------------------------- dashboard

    public async Task<OverviewKpis> KpisAsync(int days, CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        var since = DateTime.UtcNow.AddDays(-days);
        var revenue = await own.Orders.Where(o => OrderStatuses.PaidOrLater.Contains(o.Status) && o.PaidAt >= since)
            .SumAsync(o => (decimal?)o.Total, ct) ?? 0m;
        var orders = await own.Orders.CountAsync(o => o.CreatedAt >= since && o.Status != OrderStatuses.Cancelled, ct);
        var exposure = await own.Installments.Where(i => i.Plan.Status == PayPlanStatuses.Active && i.Status != InstallmentStatuses.Paid)
            .SumAsync(i => (decimal?)i.Amount, ct) ?? 0m;
        return new OverviewKpis(revenue, orders, orders == 0 ? 0m : Math.Round(revenue / orders, 2, MidpointRounding.AwayFromZero), exposure);
    }

    public async Task<List<LowStockRow>> LowStockAsync(CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await own.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Status == ProductStatuses.Active && p.Stock <= p.LowStockThreshold)
            .OrderBy(p => p.Stock)
            .Select(p => new LowStockRow(p.Id, p.Name, p.Brand, p.Stock, p.LowStockThreshold))
            .Take(8)
            .ToListAsync(ct);
    }

    /// The "needs attention" counts, in one round trip.
    public async Task<Attention> AttentionAsync(CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        return await own.OneRow.Select(_ => new Attention(
                own.Orders.Count(o => o.Status == OrderStatuses.Paid),
                own.Deliveries.Count(d => d.Status == DeliveryStatuses.Unassigned && d.Order.Status != OrderStatuses.Cancelled),
                own.Tickets.Count(t => t.ResolvedAt == null && t.DueAt < now),
                own.Products.IgnoreQueryFilters().Count(p => p.Status == ProductStatuses.Active && p.Stock <= p.LowStockThreshold),
                own.WarrantyClaims.Count(c => c.Status == WarrantyClaimStatuses.Submitted),
                own.InsuranceClaims.Count(c => c.Status == InsuranceClaimStatuses.Submitted),
                own.Users.Count(u => u.EmailVerifiedAt == null)))
            .FirstAsync(ct);
    }

    // ---------------------------------------------------------------- catalog list + edit

    public async Task<CatalogData> GetCatalogAsync(string? q, string? status, int? page, CancellationToken ct)
    {
        var all = db.Products.IgnoreQueryFilters().AsNoTracking();
        var query = all;
        if (q?.Trim() is { Length: > 0 } term) query = StoreService.Matching(query, term);
        query = status switch
        {
            ProductStatuses.Active or ProductStatuses.Draft or ProductStatuses.Archived => query.Where(p => p.Status == status),
            "low" => query.Where(p => p.Status != ProductStatuses.Archived && p.Stock <= p.LowStockThreshold),
            _ => query,
        };
        var products = await query
            .OrderByDescending(p => p.UpdatedAt).ThenBy(p => p.Name)
            .Select(p => new AdminProductRow(p.Id, p.Name, p.Brand, p.Slug, p.Category.Name, p.Category.Icon, p.Price, p.DiscountPercent,
                p.Stock, p.LowStockThreshold, p.SoldCount, p.Status, p.IsInsurable, p.WarrantyMonths, p.ImageUrl, p.UpdatedAt))
            .ToPagedAsync(page, CatalogPageSize, ct);
        var counts = await all.GroupBy(_ => 1).Select(g => new CatalogCounts(
                g.Count(),
                g.Count(p => p.Status == ProductStatuses.Active),
                g.Count(p => p.Status == ProductStatuses.Draft),
                g.Count(p => p.Status == ProductStatuses.Archived),
                g.Count(p => p.Status != ProductStatuses.Archived && p.Stock <= p.LowStockThreshold)))
            .FirstOrDefaultAsync(ct) ?? new CatalogCounts(0, 0, 0, 0, 0);
        return new CatalogData(products, await CategoriesAsync(ct), counts);
    }

    public async Task<List<AdminCategoryRow>> CategoriesAsync(CancellationToken ct) =>
        await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder)
            .Select(c => new AdminCategoryRow(c.Id, c.Name, c.Slug, c.Icon, c.Tagline, c.IsActive)).ToListAsync(ct);

    public async Task<AdminProductDetail?> ProductAsync(Guid id, CancellationToken ct)
    {
        var detail = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == id)
            .Select(p => new AdminProductDetail(
                new EditingProduct(p.Id, p.Name, p.Brand, p.CategoryId, p.Summary, p.Description, p.Price, p.DiscountPercent,
                    p.Stock, p.LowStockThreshold, p.WarrantyMonths, p.IsInsurable, p.Keywords, p.Status, p.SpecsJson,
                    p.Images.OrderBy(i => i.SortOrder).Select(i => new ProductImageRow(i.AssetId, i.Url)).ToList()),
                p.Slug, p.Category.Name, p.Category.Icon, p.SoldCount, p.RatingAvg, p.RatingCount, p.CreatedAt, p.UpdatedAt, p.ArchivedAt))
            .FirstOrDefaultAsync(ct);
        if (detail is not { Product.Images.Count: 0 }) return detail;
        // A cover set before galleries existed has no gallery row: show it as the first photo so saving keeps it.
        var cover = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == id && p.ImageAsset != null)
            .Select(p => new { p.ImageAssetId, p.ImageUrl, p.ImageAsset!.Key }).FirstOrDefaultAsync(ct);
        return cover is null ? detail
            : detail with { Product = detail.Product with { Images = [new(cover.ImageAssetId!.Value, cover.ImageUrl ?? storage.Url(cover.Key))] } };
    }

    public async Task<List<StockMovementRow>> MovementsAsync(Guid productId, CancellationToken ct) =>
        await db.StockMovements.AsNoTracking().Where(m => m.ProductId == productId)
            .OrderByDescending(m => m.At).ThenByDescending(m => m.Id).Take(25)
            .Select(m => new StockMovementRow(m.Id, m.Delta, m.Reason, m.Note, m.Order != null ? m.Order.Number : null,
                db.Users.Where(u => u.Id == m.ByUserId).Select(u => u.Name).FirstOrDefault(), m.At))
            .ToListAsync(ct);

    public async Task<SaveResult> CreateProductAsync(ProductInput input, Guid byUserId, CancellationToken ct)
    {
        if (await AdminValidators.ProductErrorAsync(db, input, ct) is { } bad) return SaveResult.Invalid(bad.Field, bad.Message);

        var product = new Product
        {
            Slug = await UniqueSlugAsync(input.Name!, null, ct),
            Name = input.Name!.Trim(),
            Brand = input.Brand!.Trim(),
            CategoryId = input.CategoryId,
            Summary = input.Summary?.Trim() ?? "",
            Description = input.Description?.Trim() ?? "",
            Price = input.Price!.Value,
            DiscountPercent = Pricing.ClampDiscount(input.DiscountPercent ?? 0),
            SpecsJson = NormalizeSpecs(input.Specs),
            WarrantyMonths = Math.Clamp(input.WarrantyMonths ?? 0, 0, 36),
            IsInsurable = input.IsInsurable ?? false,
            Stock = Math.Max(input.Stock ?? 0, 0),
            LowStockThreshold = Math.Max(input.LowStockThreshold ?? 5, 0),
            Keywords = input.Keywords?.Trim(),
            Status = input.Status == ProductStatuses.Active ? ProductStatuses.Active : ProductStatuses.Draft,
        };
        if (await SyncImagesAsync(product, [], input.ImageAssetIds, byUserId, ct) is { } imageError) return imageError;
        db.Products.Add(product);
        if (product.Stock > 0)
            db.StockMovements.Add(new StockMovement { ProductId = product.Id, Delta = product.Stock, Reason = StockReasons.Restock, Note = "Initial stock on create", ByUserId = byUserId });
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(StoreCache.Catalog, ct);
        await cache.RemoveByTagAsync(StoreCache.Home, ct);
        return SaveResult.Success($"{product.Name} created.", $"/admin/catalog/{product.Id}");
    }

    public async Task<SaveResult> EditProductAsync(Guid id, ProductInput input, Guid byUserId, CancellationToken ct)
    {
        var product = await db.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return SaveResult.Missing;
        if (await AdminValidators.ProductErrorAsync(db, input, ct) is { } bad) return SaveResult.Invalid(bad.Field, bad.Message);
        var current = await db.ProductImages.Where(i => i.ProductId == id).ToListAsync(ct);
        if (await SyncImagesAsync(product, current, input.ImageAssetIds, byUserId, ct) is { } imageError) return imageError;

        var now = DateTime.UtcNow;
        var name = input.Name!.Trim();
        if (name != product.Name) product.Slug = await UniqueSlugAsync(name, product.Id, ct);
        product.Name = name;
        product.Brand = input.Brand!.Trim();
        product.CategoryId = input.CategoryId;
        product.Summary = input.Summary?.Trim() ?? product.Summary;
        product.Description = input.Description?.Trim() ?? product.Description;
        product.Price = input.Price!.Value;
        product.DiscountPercent = Pricing.ClampDiscount(input.DiscountPercent ?? 0);
        product.SpecsJson = NormalizeSpecs(input.Specs);
        product.WarrantyMonths = Math.Clamp(input.WarrantyMonths ?? product.WarrantyMonths, 0, 36);
        product.IsInsurable = input.IsInsurable ?? product.IsInsurable;
        product.LowStockThreshold = Math.Max(input.LowStockThreshold ?? product.LowStockThreshold, 0);
        product.Keywords = input.Keywords?.Trim() ?? product.Keywords;
        if (input.Status is ProductStatuses.Draft or ProductStatuses.Active or ProductStatuses.Archived && input.Status != product.Status)
        {
            product.Status = input.Status;
            product.ArchivedAt = input.Status == ProductStatuses.Archived ? now : null;
            if (input.Status == ProductStatuses.Active) product.PublishedAt = now;
        }
        product.UpdatedAt = now;

        // Stock changes go through the movements ledger so SUM(movements) stays the truth.
        if (input.Stock is { } target && target >= 0 && target != product.Stock)
        {
            db.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id,
                Delta = target - product.Stock,
                Reason = StockReasons.Adjustment,
                Note = $"Admin adjustment {product.Stock} → {target}",
                ByUserId = byUserId,
                At = now,
            });
            product.Stock = target;
        }
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(StoreCache.Catalog, ct);
        await cache.RemoveByTagAsync(StoreCache.Home, ct);
        await cache.RemoveByTagAsync(StoreCache.Product(product.Id), ct);
        return SaveResult.Success($"{product.Name} saved.", $"/admin/catalog/{product.Id}");
    }

    /// Replaces the gallery with `assetIds` (null keeps it). Images must be ready uploads by this admin, or already on the
    /// product (so a teammate's photos survive an edit). The first image is the cover used by cards and lists.
    async Task<SaveResult?> SyncImagesAsync(Product product, List<ProductImage> current, List<Guid>? assetIds, Guid byUserId, CancellationToken ct)
    {
        if (assetIds is null) return null;
        var ids = assetIds.Distinct().ToList();
        if (ids.Count > ProductImage.MaxPerProduct) return SaveResult.Invalid("images", $"Add up to {ProductImage.MaxPerProduct} images.");
        var attached = current.Select(i => i.AssetId).Append(product.ImageAssetId ?? Guid.Empty).ToList();
        var keys = await db.Assets.AsNoTracking()
            .Where(a => ids.Contains(a.Id) && a.Status == AssetStatus.Ready && a.ContentType.StartsWith("image/")
                && (a.OwnerId == byUserId || attached.Contains(a.Id)))
            .ToDictionaryAsync(a => a.Id, a => a.Key, ct);
        if (keys.Count != ids.Count) return SaveResult.Invalid("images", "An image is missing or still uploading. Remove it and try again.");

        db.ProductImages.RemoveRange(current.Where(i => !ids.Contains(i.AssetId)));
        for (var i = 0; i < ids.Count; i++)
        {
            if (current.FirstOrDefault(c => c.AssetId == ids[i]) is { } kept) kept.SortOrder = i;
            else db.ProductImages.Add(new ProductImage { ProductId = product.Id, AssetId = ids[i], Url = storage.Url(keys[ids[i]]), SortOrder = i });
        }
        product.ImageAssetId = ids.Count > 0 ? ids[0] : null;
        product.ImageUrl = ids.Count > 0 ? current.FirstOrDefault(c => c.AssetId == ids[0])?.Url ?? storage.Url(keys[ids[0]]) : null;
        return null;
    }

    public async Task<SaveResult> RestockAsync(Guid id, RestockInput input, Guid byUserId, CancellationToken ct)
    {
        var delta = input.Delta;
        var product = await db.Products.IgnoreQueryFilters().Where(p => p.Id == id)
            .Select(p => new { p.LowStockThreshold }).FirstOrDefaultAsync(ct);
        if (product is null) return SaveResult.Missing;
        if (delta == 0) return SaveResult.Error("Change the quantity first.");

        var at = DateTime.UtcNow;
        db.StockMovements.Add(new StockMovement
        {
            ProductId = id,
            Delta = delta,
            Reason = delta > 0 ? StockReasons.Restock : StockReasons.Damage,
            Note = string.IsNullOrWhiteSpace(input.Note) ? (delta > 0 ? "Manual restock" : "Stock write-off") : input.Note.Trim(),
            ByUserId = byUserId,
            At = at,
        });
        await db.Products.IgnoreQueryFilters().Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + delta).SetProperty(p => p.UpdatedAt, at), ct);
        await db.SaveChangesAsync(ct);
        var stock = await db.Products.IgnoreQueryFilters().Where(p => p.Id == id).Select(p => p.Stock).FirstAsync(ct);
        await cache.RemoveByTagAsync(StoreCache.Catalog, ct);
        await cache.RemoveByTagAsync(StoreCache.Home, ct);
        await cache.RemoveByTagAsync(StoreCache.Product(id), ct);
        if (stock > product.LowStockThreshold) return SaveResult.Success("Quantity saved.");
        await realtime.StockAlertAsync(id, stock, ct);
        return SaveResult.Success("Quantity saved — low stock alert fired.");
    }

    // ---------------------------------------------------------------- categories

    public async Task<SaveResult> CreateCategoryAsync(CategoryInput input, CancellationToken ct)
    {
        if (input.Name?.Trim() is not { Length: >= 2 } name)
            return SaveResult.Invalid("name", "Category name is required.");
        var slug = Slugify(name);
        if (await db.Categories.AnyAsync(c => c.Slug == slug, ct))
            return SaveResult.Invalid("name", "Slug is taken.");
        db.Categories.Add(new Category
        {
            Name = name,
            Slug = slug,
            Tagline = input.Tagline?.Trim() ?? "",
            Icon = string.IsNullOrWhiteSpace(input.Icon) ? "Package" : input.Icon.Trim(),
            SortOrder = (await db.Categories.MaxAsync(c => (int?)c.SortOrder, ct)) + 1 ?? 0,
        });
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(StoreCache.Catalog, ct);
        await cache.RemoveByTagAsync(StoreCache.Home, ct);
        return SaveResult.Success("Category added.");
    }

    public async Task<SaveResult> EditCategoryAsync(Guid id, CategoryInput input, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return SaveResult.Missing;
        if (!string.IsNullOrWhiteSpace(input.Name)) category.Name = input.Name.Trim();
        if (input.Tagline is not null) category.Tagline = input.Tagline.Trim();
        if (!string.IsNullOrWhiteSpace(input.Icon)) category.Icon = input.Icon.Trim();
        if (input.IsActive is { } active) category.IsActive = active;
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(StoreCache.Catalog, ct);
        await cache.RemoveByTagAsync(StoreCache.Home, ct);
        return SaveResult.Success("Category saved.");
    }

    // ---------------------------------------------------------------- helpers

    static string Slugify(string value)
    {
        var slug = string.Concat(value.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')).Trim('-');
        return slug.Length == 0 ? "product" : slug;
    }

    async Task<string> UniqueSlugAsync(string name, Guid? productId, CancellationToken ct)
    {
        var baseSlug = Slugify(name);
        var slug = baseSlug;
        for (var n = 2; await db.Products.IgnoreQueryFilters().AnyAsync(p => p.Slug == slug && p.Id != productId, ct); n++)
            slug = $"{baseSlug}-{n}";
        return slug;
    }

    /// The form posts specs as a JSON array of {label, value}; keep clean rows only.
    static string NormalizeSpecs(string? json)
    {
        try
        {
            var rows = JsonSerializer.Deserialize<List<SpecRow>>(json ?? "[]", JsonSerializerOptions.Web) ?? [];
            return JsonSerializer.Serialize(rows
                .Where(r => !string.IsNullOrWhiteSpace(r.Label) && !string.IsNullOrWhiteSpace(r.Value))
                .Select(r => new SpecRow(r.Label.Trim(), r.Value.Trim())).Take(30), JsonSerializerOptions.Web);
        }
        catch (JsonException) { return "[]"; }
    }
}
