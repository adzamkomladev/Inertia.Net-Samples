using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Admin
{
    static readonly TimeSpan KpiTtl = TimeSpan.FromSeconds(60);

    public static void MapAdmin(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireAuthorization("admin");
        admin.MapGet("/", Overview);
        admin.MapGet("/analytics", Analytics);

        var catalog = admin.MapGroup("/catalog");
        catalog.MapGet("/", Catalog);
        catalog.MapPost("/", (ProductInput input, AdminService svc, CancellationToken ct) => svc.CreateProductAsync(input, ct));
        catalog.MapPost("/{id:guid}", (Guid id, ProductInput input, ClaimsPrincipal user, AdminService svc, CancellationToken ct) =>
            svc.EditProductAsync(id, input, user.UserId(), ct));
        catalog.MapPost("/{id:guid}/restock", (Guid id, RestockInput input, ClaimsPrincipal user, AdminService svc, CancellationToken ct) =>
            svc.RestockAsync(id, input, user.UserId(), ct));

        admin.MapPost("/categories", (CategoryInput input, AdminService svc, CancellationToken ct) => svc.CreateCategoryAsync(input, ct));
        admin.MapPost("/categories/{id:guid}", (Guid id, CategoryInput input, AdminService svc, CancellationToken ct) => svc.EditCategoryAsync(id, input, ct));
    }

    // The KPI loaders are independent queries on their own contexts. The attention counts stay uncached
    // so SignalR-triggered reloads always show live numbers.
    static IResult Overview(string? range, AdminService admin, SalesService sales)
    {
        var days = range == "7" ? 7 : 30;
        return Render("admin/Index", new InertiaProps
        {
            ["range"] = days,
            ["kpis"] = Defer(ct => admin.KpisAsync(days, ct), "kpis").Parallel().Cache($"admin:kpis:{days}", KpiTtl),
            ["revenueSeries"] = Defer(ct => sales.SeriesAsync(days, ct), "kpis").Parallel().Cache($"sales:series:{days}", KpiTtl),
            ["lowStock"] = Defer(admin.LowStockAsync, "kpis").Parallel().Cache("admin:low-stock", KpiTtl),
            ["attention"] = Defer(admin.AttentionAsync, "kpis").Parallel(),
        });
    }

    static IResult Analytics(string? range, SalesService sales)
    {
        var days = range is "7" or "90" ? int.Parse(range) : 30;
        return Render("admin/Analytics", new InertiaProps
        {
            ["range"] = days,
            ["kpis"] = Defer(ct => sales.KpisAsync(days, ct), "kpis").Parallel().Cache($"sales:kpis:{days}", KpiTtl),
            ["series"] = Defer(ct => sales.SeriesAsync(days, ct), "charts").Parallel().Cache($"sales:series:{days}", KpiTtl),
            ["topProducts"] = Defer(ct => sales.TopProductsAsync(days, ct), "charts").Parallel().Cache($"sales:top-products:{days}", KpiTtl),
            ["categoryShare"] = Defer(ct => sales.CategoryShareAsync(days, ct), "charts").Parallel().Cache($"sales:category-share:{days}", KpiTtl),
        });
    }

    static async Task<IResult> Catalog(string? q, int? page, [FromQuery] Guid? edit, AdminService svc, CancellationToken ct)
    {
        var data = await svc.GetCatalogAsync(q, page, edit, ct);
        return Render("admin/Catalog", new InertiaProps
        {
            ["products"] = data.Products,
            ["q"] = q ?? "",
            ["categories"] = data.Categories,
            ["editing"] = data.Editing,
        });
    }
}
