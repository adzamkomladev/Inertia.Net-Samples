using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using System.Text.Json;
using VuePgBlog.Data;
using VuePgBlog.Infrastructure;

namespace VuePgBlog.Features;

/// Admin dashboard: KPI overview with a period switch, catalog CRUD with stock ledger, live needs-attention strip.
public static partial class AdminService
{
    internal static async Task<IResult> OverviewAsync(string? range, AppDb db, CancellationToken ct)
    {
        var days = range == "7" ? 7 : 30;
        return Render(
            "admin/Index",
            new InertiaProps
            {
                ["range"] = days == 7 ? "7" : "30",
                // Cached prop (Inertia.Net 1.4): kept as JSON in IMemoryCache for a minute — admin dashboards
                // reload far more often than sales data changes shape.
                ["kpis"] = Defer(token => BuildKpisAsync(db, days, token), "kpis")
                    .Cache($"admin:{days}:kpis", TimeSpan.FromSeconds(60)),
            }
        );
    }

    /// KPIs + the needs-attention strip. Needs-attention stays uncached so SignalR reloads always read live counts.
    internal static async Task<object> BuildKpisAsync(AppDb db, int days, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        var paidStatuses = new[] { OrderStatuses.Paid, OrderStatuses.Processing, OrderStatuses.Shipped, OrderStatuses.Delivered };

        var revenue = await db.Orders.AsNoTracking()
            .Where(o => paidStatuses.Contains(o.Status) && o.PaidAt != null && o.PaidAt >= since)
            .SumAsync(o => (decimal?)o.Total, ct) ?? 0m;
        var orderCount = await db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= since && o.Status != OrderStatuses.Cancelled)
            .CountAsync(ct);
        var planExposure = await db.Installments.AsNoTracking()
            .Where(i => i.Plan.Status == PayPlanStatuses.Active && i.Status != InstallmentStatuses.Paid)
            .SumAsync(i => (decimal?)i.Amount, ct) ?? 0m;
        var lowStock = await db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Status == ProductStatuses.Active && p.Stock <= p.LowStockThreshold)
            .OrderBy(p => p.Stock)
            .Select(p => new { p.Id, p.Name, p.Stock, p.LowStockThreshold, Brand = p.Brand })
            .Take(8)
            .ToListAsync(ct);

        var unpaid = PaymentStatuses.Unpaid;
        var now = DateTime.UtcNow;
        return new
        {
            Revenue = revenue,
            Orders = orderCount,
            Aov = orderCount == 0 ? 0m : Math.Round(revenue / orderCount, 2, MidpointRounding.AwayFromZero),
            PlanExposure = planExposure,
            RevenueSeries = await db.SalesDailyStats.AsNoTracking()
                .Where(s => s.Date >= DateOnly.FromDateTime(since))
                .OrderBy(s => s.Date)
                .Select(s => new { s.Date, s.Revenue, s.OrderCount, s.BnplShare })
                .ToListAsync(ct),
            LowStock = lowStock,
            Attention = new
            {
                OrdersToConfirm = await db.Orders.AsNoTracking().CountAsync(o => o.Status == OrderStatuses.Paid, ct),
                DeliveriesToAssign = await db.Deliveries.AsNoTracking().CountAsync(d => d.Status == DeliveryStatuses.Unassigned && d.Order.Status != OrderStatuses.Cancelled, ct),
                OverdueTickets = await db.Tickets.AsNoTracking().CountAsync(t => t.ResolvedAt == null && t.DueAt < now, ct),
                LowStockCount = await db.Products.IgnoreQueryFilters().AsNoTracking().CountAsync(p => p.Status == ProductStatuses.Active && p.Stock <= p.LowStockThreshold, ct),
                WarrantyClaims = await db.WarrantyClaims.AsNoTracking().CountAsync(c => c.Status == WarrantyClaimStatuses.Submitted, ct),
                InsuranceClaims = await db.InsuranceClaims.AsNoTracking().CountAsync(c => c.Status == InsuranceClaimStatuses.Submitted, ct),
                UnverifiedUsers = await db.Users.AsNoTracking().CountAsync(u => u.EmailVerifiedAt == null && u.Email != null, ct),
            },
        };
    }

    // ---------------------------------------------------------------- catalog list + edit

    internal static async Task<IResult> CatalogAsync(string? q, int? page, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var pageNo = Math.Max(page ?? 1, 1);
        var size = 20;
        var query = db.Products.IgnoreQueryFilters().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(p => EF.Functions.ILike(p.Name, pattern) || EF.Functions.ILike(p.Brand, pattern));
        }
        var rows = await query
            .OrderByDescending(p => p.UpdatedAt).ThenBy(p => p.Name)
            .Skip((pageNo - 1) * size).Take(size + 1)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Brand,
                Slug = p.Slug,
                Category = p.Category.Name,
                p.Price,
                p.DiscountPercent,
                p.Stock,
                p.LowStockThreshold,
                p.SoldCount,
                p.Status,
                p.IsInsurable,
                p.WarrantyMonths,
            })
            .ToListAsync(ct);
        var categories = await db.Categories.OrderBy(c => c.SortOrder).Select(c => new { c.Id, c.Name, c.Slug, c.Icon, c.Tagline, c.IsActive }).ToListAsync(ct);
        object? editing = null;
        if (ctx.Request.Query.TryGetValue("edit", out var edit) && Guid.TryParse(edit, out var editId))
        {
            editing = await db.Products.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.Id == editId)
                .Select(p => new {
                    p.Id, p.Name, p.Brand, p.CategoryId, p.Summary, p.Description, p.Price,
                    p.DiscountPercent, p.Stock, p.LowStockThreshold, p.WarrantyMonths, p.IsInsurable,
                    p.Keywords, p.Status, p.SpecsJson, SearchableSpecs = SearchIndexing.SearchProductSpecs(p.SpecsJson),
                })
                .FirstOrDefaultAsync(ct);
        }
        return Render(
            "admin/Catalog",
            new InertiaProps
            {
                ["products"] = new AdminCatalogPage(rows.Take(size).Select(r => (object)r).ToList(), pageNo, rows.Count > size),
                ["q"] = q ?? "",
                ["categories"] = categories,
                ["editing"] = editing,
            }
        );
    }

    internal static async Task<IResult> CreateProductAsync(ProductInput input, HttpContext ctx, AppDb db, JobQueue jobs, CancellationToken ct)
    {
        if (await db.Categories.IgnoreQueryFilters().AnyAsync(c => c.Id == input.CategoryId, ct) is false)
            return Flash(new Toast("error", "Pick a category."));
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Brand))
            return Flash(new Toast("error", "Name and brand are required."));
        if (input.Price is not { } price || price <= 0)
            return Flash(new Toast("error", "Price must be greater than zero."));

        var now = DateTime.UtcNow;
        var slug = await UniqueSlug(db, input.Name!, null, ct);
        var product = new Product
        {
            Slug = slug,
            Name = input.Name!.Trim(),
            Brand = input.Brand!.Trim(),
            CategoryId = input.CategoryId,
            Summary = input.Summary?.Trim() ?? "",
            Description = input.Description?.Trim() ?? "",
            Price = price,
            DiscountPercent = Pricing.ClampDiscount(input.DiscountPercent ?? 0),
            SpecsJson = SpecJson(input.Specs),
            WarrantyMonths = Math.Clamp(input.WarrantyMonths ?? 0, 0, 36),
            IsInsurable = input.IsInsurable ?? false,
            Stock = Math.Max(input.Stock ?? 0, 0),
            LowStockThreshold = Math.Max(input.LowStockThreshold ?? 5, 0),
            Keywords = input.Keywords?.Trim(),
            ImageAssetId = input.ImageAssetId,
            Status = input.Status == ProductStatuses.Active ? ProductStatuses.Active : ProductStatuses.Draft,
            PublishedAt = now,
        };
        if (product.Stock > 0)
            db.StockMovements.Add(new StockMovement
            { ProductId = product.Id, Delta = product.Stock, Reason = StockReasons.Restock, Note = "Initial stock on create", At = now });
        db.Products.Add(product);
        await SearchIndexing.RequestAsync(db, jobs, product.Id, product.IndexVersion, ct);
        ctx.Inertia().Flash("toast", new Toast("success", $"{product.Name} created."));
        return Results.Redirect("/admin/catalog");
        static IResult Flash(Toast toast) => Back().WithFlash("toast", toast);
    }

    internal static async Task<IResult> EditProductAsync(Guid id, ProductInput input, HttpContext ctx, AppDb db, JobQueue jobs, CancellationToken ct)
    {
        var product = await db.Products.IgnoreQueryFilters().Include(p => p.Category).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Brand))
            return Back().WithErrors(new Dictionary<string, string> { ["name"] = "Name and brand are required." });
        if (input.Price is not { } price || price <= 0)
            return Back().WithErrors(new Dictionary<string, string> { ["price"] = "Price must be greater than zero." });
        if (await db.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == input.CategoryId, ct) is null)
            return Back().WithErrors(new Dictionary<string, string> { ["categoryId"] = "Pick a category." });

        var now = DateTime.UtcNow;
        var oldName = product.Name;
        var oldSlug = product.Slug;
        product.Name = input.Name!.Trim();
        product.Brand = input.Brand!.Trim();
        product.CategoryId = input.CategoryId;
        if (!product.Name.Equals(oldName, StringComparison.Ordinal))
            product.Slug = await UniqueSlug(db, product.Name, product.Id, ct);
        else
            product.Slug = oldSlug;
        product.Summary = input.Summary?.Trim() ?? product.Summary;
        product.Description = input.Description?.Trim() ?? product.Description;
        product.Price = price;
        product.DiscountPercent = Pricing.ClampDiscount(input.DiscountPercent ?? 0);
        product.SpecsJson = SpecJson(input.Specs);
        product.WarrantyMonths = Math.Clamp(input.WarrantyMonths ?? product.WarrantyMonths, 0, 36);
        product.IsInsurable = input.IsInsurable ?? product.IsInsurable;
        product.LowStockThreshold = Math.Max(input.LowStockThreshold ?? product.LowStockThreshold, 0);
        product.Keywords = input.Keywords?.Trim() ?? product.Keywords;
        product.ImageAssetId = input.ImageAssetId ?? product.ImageAssetId;
        if (!string.IsNullOrWhiteSpace(input.Status)
            && (input.Status is ProductStatuses.Draft or ProductStatuses.Active or ProductStatuses.Archived)
            && input.Status != product.Status)
        {
            product.Status = input.Status;
            product.ArchivedAt = input.Status == ProductStatuses.Archived ? now : null;
            if (input.Status == ProductStatuses.Active) product.PublishedAt = now;
        }
        product.UpdatedAt = now;

        // Stock target: adjust through the movements ledger so SUM(movements) stays the truth.
        if (input.Stock is { } target && target >= 0 && target != product.Stock)
        {
            var delta = target - product.Stock;
            db.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id,
                Delta = delta,
                Reason = StockReasons.Adjustment,
                Note = $"Admin adjustment {product.Stock} → {target}",
                ByUserId = ctx.User.UserIdOrNull(),
                At = now,
            });
            product.Stock = target;
        }

        // Content/status changes re-request a search sync at the bumped version.
        product.IndexVersion++;
        await SearchIndexing.RequestAsync(db, jobs, product.Id, product.IndexVersion, ct);
        ctx.Inertia().Flash("toast", new Toast("success", $"{product.Name} saved."));
        return Results.Redirect("/admin/catalog");
    }

    internal static async Task<IResult> RestockAsync(Guid id, int delta, string? note, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        var product = await db.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (delta == 0)
            return Back().WithFlash("toast", new Toast("error", "Change the quantity first."));
        var at = DateTime.UtcNow;
        var reason = delta > 0 ? StockReasons.Restock : StockReasons.Damage;
        db.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id,
            Delta = delta,
            Reason = reason,
            Note = string.IsNullOrWhiteSpace(note) ? (delta > 0 ? "Manual restock" : "Stock write-off") : note.Trim(),
            ByUserId = ctx.User.UserIdOrNull(),
            At = at,
        });
        await db.Products.IgnoreQueryFilters().Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Stock, p => p.Stock + delta)
                .SetProperty(p => p.UpdatedAt, at), ct);
        await db.SaveChangesAsync(ct);
        var stockNow = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == id).Select(p => p.Stock).FirstAsync(ct);
        if (stockNow <= product.LowStockThreshold)
            await realtime.StockAlertAsync(id, stockNow, ct);
        return Back().WithFlash("toast", new Toast("success", stockNow > product.LowStockThreshold ? "Quantity saved." : "Quantity saved — low stock alert fired."));
    }

    // ---------------------------------------------------------------- categories

    internal static async Task<IResult> CreateCategoryAsync(string? name, string? tagline, string? icon, AppDb db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2)
            return Back().WithErrors(new Dictionary<string, string> { ["name"] = "Category name is required." });
        var nameTrim = name.Trim();
        if (await db.Categories.IgnoreQueryFilters().AnyAsync(c => c.Slug == Slugify(nameTrim), ct))
            return Back().WithErrors(new Dictionary<string, string> { ["name"] = "Slug is taken." });
        db.Categories.Add(new Category
        {
            Name = nameTrim,
            Slug = Slugify(nameTrim),
            Tagline = tagline?.Trim() ?? "",
            Icon = string.IsNullOrWhiteSpace(icon) ? "Package" : icon.Trim(),
            SortOrder = (await db.Categories.MaxAsync(c => (int?)c.SortOrder, ct)) + 1 ?? 0,
        });
        await db.SaveChangesAsync(ct);
        return Back().WithFlash("toast", new Toast("success", "Category added."));
    }

    internal static async Task<IResult> EditCategoryAsync(Guid id, string? name, string? tagline, string? icon, bool? isActive, AppDb db, CancellationToken ct)
    {
        var category = await db.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (!string.IsNullOrWhiteSpace(name)) category.Name = name.Trim();
        if (tagline is not null) category.Tagline = tagline.Trim();
        if (!string.IsNullOrWhiteSpace(icon)) category.Icon = icon.Trim();
        if (isActive is { } active) category.IsActive = active;
        await db.SaveChangesAsync(ct);
        return Back().WithFlash("toast", new Toast("success", "Category saved."));
    }

    // ---------------------------------------------------------------- helpers

    static string Slugify(string value)
    {
        var slug = new string(value.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) && c != '-' ? c : '-').ToArray()).Trim('-');
        return string.IsNullOrEmpty(slug) ? "product" : slug;
    }

    static async Task<string> UniqueSlug(AppDb db, string name, Guid? productId, CancellationToken ct)
    {
        var baseSlug = Slugify(name);
        var slug = baseSlug;
        var n = 1;
        while (await db.Products.IgnoreQueryFilters().AsNoTracking().AnyAsync(p => p.Slug == slug && (productId == null || p.Id != productId), ct))
            slug = $"{baseSlug}-{++n}";
        return slug;
    }

    /// "Port: USB-C, Weight: 1.2kg" text input → specs json rows.
    internal static string SpecJson(string? specs)
    {
        if (string.IsNullOrWhiteSpace(specs)) return "[]";
        var rows = specs.Split('\n', ',')
            .Select(line => (line, i: line.IndexOf(':')))
            .Where(pair => pair.i > 0 && pair.i < pair.line.Length - 1)
            .Select(pair => new SearchIndexing.SpecRow(pair.line[..pair.i].Trim(), pair.line[(pair.i + 1)..].Trim()))
            .Where(spec => spec.Label.Length > 0 && spec.Value.Length > 0)
            .Take(30)
            .ToList();
        return rows.Count == 0 ? "[]" : JsonSerializer.Serialize(rows);
    }
}

public sealed record AdminCatalogPage(IReadOnlyList<object> Data, int Page, bool HasMore) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}

/// Create/edit payload for catalog items; stock is always adjusted through movements, never mutated directly.
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
