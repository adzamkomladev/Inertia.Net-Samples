namespace VuePgBlog.Features;

public static class Admin
{
    public static void MapAdmin(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireAuthorization("admin");
        admin.MapGet("/", AdminService.OverviewAsync);
        admin.MapGet("/analytics", SalesService.ReportAsync);

        var catalog = admin.MapGroup("/catalog");
        catalog.MapGet("/", AdminService.CatalogAsync);
        catalog.MapPost("/", AdminService.CreateProductAsync);
        catalog.MapPost("/{id:guid}", AdminService.EditProductAsync);
        catalog.MapPost("/{id:guid}/restock", AdminService.RestockAsync);

        admin.MapPost("/categories", AdminService.CreateCategoryAsync);
        admin.MapPost("/categories/{id:guid}", AdminService.EditCategoryAsync);
    }
}
