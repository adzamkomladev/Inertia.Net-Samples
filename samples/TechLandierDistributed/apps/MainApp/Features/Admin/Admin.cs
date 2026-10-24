using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Admin
{
    public static void MapAdmin(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireAuthorization("admin");
        admin.MapGet("/", Overview);
        admin.MapGet("/analytics", Analytics);

        var catalog = admin.MapGroup("/catalog");
        catalog.MapGet("/", Catalog);
        catalog.MapGet("/{id:guid}", Product);
        catalog.MapPost("/", async (ProductInput input, ClaimsPrincipal user, AdminService svc, CancellationToken ct) => await svc.CreateProductAsync(input, user.UserId(), ct));
        catalog.MapPost("/{id:guid}", async (Guid id, ProductInput input, ClaimsPrincipal user, AdminService svc, CancellationToken ct) =>
            await svc.EditProductAsync(id, input, user.UserId(), ct));
        catalog.MapPost("/{id:guid}/restock", async (Guid id, RestockInput input, ClaimsPrincipal user, AdminService svc, CancellationToken ct) =>
            await svc.RestockAsync(id, input, user.UserId(), ct));

        admin.MapPost("/categories", async (CategoryInput input, AdminService svc, CancellationToken ct) => await svc.CreateCategoryAsync(input, ct));
        admin.MapPost("/categories/{id:guid}", async (Guid id, CategoryInput input, AdminService svc, CancellationToken ct) => await svc.EditCategoryAsync(id, input, ct));
    }

    // The KPI loaders are independent queries on their own contexts. The attention counts stay uncached
    // so SignalR-triggered reloads always show live numbers.
    static IResult Overview(string? range, AdminService admin, SalesService sales)
    {
        var days = range == "7" ? 7 : 30;
        return Render("admin/Index", new InertiaProps
        {
            ["range"] = days,
            ["kpis"] = Defer(async ct => await admin.KpisAsync(days, ct), "kpis").Parallel()
                .Cache($"admin:kpis:{days}", StoreCache.SalesFreshFor, StoreCache.SalesStaleFor, [StoreCache.Sales, StoreCache.Catalog]),
            ["revenueSeries"] = Defer(async ct => await sales.SeriesAsync(days, ct), "kpis").Parallel()
                .Cache($"sales:series:{days}", StoreCache.SalesFreshFor, StoreCache.SalesStaleFor, [StoreCache.Sales]),
            ["lowStock"] = Defer(admin.LowStockAsync, "kpis").Parallel()
                .Cache("admin:low-stock", StoreCache.SalesFreshFor, StoreCache.SalesStaleFor, [StoreCache.Catalog]),
            ["attention"] = Defer(admin.AttentionAsync, "kpis").Parallel(),
        });
    }

    static IResult Analytics(string? range, SalesService sales)
    {
        var days = range is "7" or "90" ? int.Parse(range) : 30;
        return Render("admin/Analytics", new InertiaProps
        {
            ["range"] = days,
            ["kpis"] = Defer(async ct => await sales.KpisAsync(days, ct), "kpis").Parallel()
                .Cache($"sales:kpis:{days}", StoreCache.SalesFreshFor, StoreCache.SalesStaleFor, [StoreCache.Sales]),
            ["series"] = Defer(async ct => await sales.SeriesAsync(days, ct), "charts").Parallel()
                .Cache($"sales:series:{days}", StoreCache.SalesFreshFor, StoreCache.SalesStaleFor, [StoreCache.Sales]),
            ["topProducts"] = Defer(async ct => await sales.TopProductsAsync(days, ct), "charts").Parallel()
                .Cache($"sales:top-products:{days}", StoreCache.SalesFreshFor, StoreCache.SalesStaleFor, [StoreCache.Sales]),
            ["categoryShare"] = Defer(async ct => await sales.CategoryShareAsync(days, ct), "charts").Parallel()
                .Cache($"sales:category-share:{days}", StoreCache.SalesFreshFor, StoreCache.SalesStaleFor, [StoreCache.Sales]),
        });
    }

    // `?edit=<id>` links from before the product page existed still land on it.
    static async Task<IResult> Catalog(string? q, string? status, int? page, [FromQuery] string? edit, AdminService svc, CancellationToken ct)
    {
        if (Guid.TryParse(edit, out var editId)) return Results.Redirect($"/admin/catalog/{editId}");
        var data = await svc.GetCatalogAsync(q, status, page, ct);
        return Render("admin/Catalog", new InertiaProps
        {
            ["products"] = data.Products,
            ["q"] = q ?? "",
            ["status"] = status ?? "all",
            ["counts"] = data.Counts,
            ["categories"] = data.Categories,
        });
    }

    static async Task<IResult> Product(Guid id, AdminService svc, CancellationToken ct)
    {
        if (await svc.ProductAsync(id, ct) is not { } detail) return RouteResults.NotFoundPage();
        return Render("admin/Product", new InertiaProps
        {
            ["detail"] = detail,
            ["categories"] = await svc.CategoriesAsync(ct),
            ["movements"] = await svc.MovementsAsync(id, ct),
        });
    }
}
