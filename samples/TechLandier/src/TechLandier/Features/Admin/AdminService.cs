using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// Create/edit payload for catalog items. Specs is a JSON array of {label, value}; stock only moves through the ledger.
public sealed record ProductInput(
    string? Name,
    string? Brand,
    Guid CategoryId,
    string? Summary,
    string? Description,
    decimal? Price,
    int? DiscountPercent,
    int? Stock,
    int? LowStockThreshold,
    int? WarrantyMonths,
    bool? IsInsurable,
    string? Keywords,
    string? Specs,
    Guid? ImageAssetId,
    string? Status);

public sealed record RestockInput(int Delta, string? Note);
public sealed record CategoryInput(string? Name, string? Tagline, string? Icon, bool? IsActive);

public sealed record OverviewKpis(decimal Revenue, int Orders, decimal Aov, decimal PlanExposure);
public sealed record LowStockRow(Guid Id, string Name, string Brand, int Stock, int LowStockThreshold);
public sealed record Attention(int OrdersToConfirm, int DeliveriesToAssign, int OverdueTickets, int LowStockCount, int WarrantyClaims, int InsuranceClaims, int UnverifiedUsers);

public sealed record AdminProductRow(Guid Id, string Name, string Brand, string Slug, string Category, decimal Price, int DiscountPercent,
    int Stock, int LowStockThreshold, int SoldCount, string Status, bool IsInsurable, int WarrantyMonths);
public sealed record AdminCategoryRow(Guid Id, string Name, string Slug, string Icon, string Tagline, bool IsActive);
public sealed record EditingProduct(Guid Id, string Name, string Brand, Guid CategoryId, string Summary, string Description, decimal Price,
    int DiscountPercent, int Stock, int LowStockThreshold, int WarrantyMonths, bool IsInsurable, string? Keywords, string Status, string SpecsJson);
public sealed record CatalogData(Paged<AdminProductRow> Products, List<AdminCategoryRow> Categories, EditingProduct? Editing);

/// Admin dashboard numbers and catalog management. The dashboard loaders run as parallel Inertia props,
/// so they use their own contexts; everything else uses the request's.
public sealed class AdminService(AppDb db, IDbContextFactory<AppDb> dbs, RealtimeEvents realtime)
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

    public async Task<CatalogData> GetCatalogAsync(string? q, int? page, Guid? editId, CancellationToken ct)
    {
        var query = db.Products.IgnoreQueryFilters().AsNoTracking();
        if (q?.Trim() is { Length: > 0 } term) query = StoreService.Matching(query, term);
        var products = await query
            .OrderByDescending(p => p.UpdatedAt).ThenBy(p => p.Name)
            .Select(p => new AdminProductRow(p.Id, p.Name, p.Brand, p.Slug, p.Category.Name, p.Price, p.DiscountPercent,
                p.Stock, p.LowStockThreshold, p.SoldCount, p.Status, p.IsInsurable, p.WarrantyMonths))
            .ToPagedAsync(page, CatalogPageSize, ct);
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder)
            .Select(c => new AdminCategoryRow(c.Id, c.Name, c.Slug, c.Icon, c.Tagline, c.IsActive)).ToListAsync(ct);
        var editing = editId is { } id
            ? await db.Products.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == id)
                .Select(p => new EditingProduct(p.Id, p.Name, p.Brand, p.CategoryId, p.Summary, p.Description, p.Price, p.DiscountPercent,
                    p.Stock, p.LowStockThreshold, p.WarrantyMonths, p.IsInsurable, p.Keywords, p.Status, p.SpecsJson))
                .FirstOrDefaultAsync(ct)
            : null;
        return new CatalogData(products, categories, editing);
    }

    public async Task<SaveResult> CreateProductAsync(ProductInput input, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == input.CategoryId, ct))
            return SaveResult.Error("Pick a category.");
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Brand))
            return SaveResult.Error("Name and brand are required.");
        if (input.Price is not { } price || price <= 0)
            return SaveResult.Error("Price must be greater than zero.");

        var product = new Product
        {
            Slug = await UniqueSlugAsync(input.Name, null, ct),
            Name = input.Name.Trim(),
            Brand = input.Brand.Trim(),
            CategoryId = input.CategoryId,
            Summary = input.Summary?.Trim() ?? "",
            Description = input.Description?.Trim() ?? "",
            Price = price,
            DiscountPercent = Pricing.ClampDiscount(input.DiscountPercent ?? 0),
            SpecsJson = NormalizeSpecs(input.Specs),
            WarrantyMonths = Math.Clamp(input.WarrantyMonths ?? 0, 0, 36),
            IsInsurable = input.IsInsurable ?? false,
            Stock = Math.Max(input.Stock ?? 0, 0),
            LowStockThreshold = Math.Max(input.LowStockThreshold ?? 5, 0),
            Keywords = input.Keywords?.Trim(),
            ImageAssetId = input.ImageAssetId,
            Status = input.Status == ProductStatuses.Active ? ProductStatuses.Active : ProductStatuses.Draft,
        };
        db.Products.Add(product);
        if (product.Stock > 0)
            db.StockMovements.Add(new StockMovement { ProductId = product.Id, Delta = product.Stock, Reason = StockReasons.Restock, Note = "Initial stock on create" });
        await db.SaveChangesAsync(ct);
        return SaveResult.Success($"{product.Name} created.", "/admin/catalog");
    }

    public async Task<SaveResult> EditProductAsync(Guid id, ProductInput input, Guid byUserId, CancellationToken ct)
    {
        var product = await db.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return SaveResult.Missing;
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Brand))
            return SaveResult.Invalid("name", "Name and brand are required.");
        if (input.Price is not { } price || price <= 0)
            return SaveResult.Invalid("price", "Price must be greater than zero.");
        if (!await db.Categories.AnyAsync(c => c.Id == input.CategoryId, ct))
            return SaveResult.Invalid("categoryId", "Pick a category.");

        var now = DateTime.UtcNow;
        var name = input.Name.Trim();
        if (name != product.Name) product.Slug = await UniqueSlugAsync(name, product.Id, ct);
        product.Name = name;
        product.Brand = input.Brand.Trim();
        product.CategoryId = input.CategoryId;
        product.Summary = input.Summary?.Trim() ?? product.Summary;
        product.Description = input.Description?.Trim() ?? product.Description;
        product.Price = price;
        product.DiscountPercent = Pricing.ClampDiscount(input.DiscountPercent ?? 0);
        product.SpecsJson = NormalizeSpecs(input.Specs);
        product.WarrantyMonths = Math.Clamp(input.WarrantyMonths ?? product.WarrantyMonths, 0, 36);
        product.IsInsurable = input.IsInsurable ?? product.IsInsurable;
        product.LowStockThreshold = Math.Max(input.LowStockThreshold ?? product.LowStockThreshold, 0);
        product.Keywords = input.Keywords?.Trim() ?? product.Keywords;
        product.ImageAssetId = input.ImageAssetId ?? product.ImageAssetId;
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
        return SaveResult.Success($"{product.Name} saved.", "/admin/catalog");
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
