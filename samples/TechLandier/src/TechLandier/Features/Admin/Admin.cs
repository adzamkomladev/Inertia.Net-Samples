using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;


public static class Admin
{
    public static void MapAdmin(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireAuthorization("admin");
        admin.MapGet("/", OverviewAsync);
        admin.MapGet("/analytics", AnalyticsAsync);

        var catalog = admin.MapGroup("/catalog");
        catalog.MapGet("/", CatalogAsync);
        catalog.MapPost("/", CreateProductAsync);
        catalog.MapPost("/{id:guid}", EditProductAsync);
        catalog.MapPost("/{id:guid}/restock", RestockAsync);

        admin.MapPost("/categories", CreateCategoryAsync);
        admin.MapPost("/categories/{id:guid}", EditCategoryAsync);
    }

    static IResult Finish(SaveResult r) => r switch
    {
        { NotFound: true } => Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404),
        { Errors: { } errors } => Back().WithErrors(errors),
        { FlashType: { } type, FlashMessage: { } message } => Back().WithFlash("toast", new Toast(type, message)),
        _ => Back(),
    };

    internal static async Task<IResult> OverviewAsync(string? range, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var days = range == "7" ? 7 : 30;
        return Render("admin/Index", new InertiaProps
        {
            ["range"] = days == 7 ? "7" : "30",
            // Cached prop (Inertia.Net 1.4): kept as JSON in IMemoryCache for a minute — admin dashboards
            // reload far more often than sales data changes shape.
            ["kpis"] = Defer(token => AdminService.BuildKpisAsync(db, days, token), "kpis")
                .Cache($"admin:{days}:kpis", TimeSpan.FromSeconds(60)),
        });
    }

    // ------------------------------------------------------------ analytics (sales rollups + live top sellers)

    internal static async Task<IResult> AnalyticsAsync(string? range, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var days = range == "90" ? 90 : range == "7" ? 7 : 30;
        var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));
        var sinceUtc = since.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var paidStatuses = new[] { OrderStatuses.Paid, OrderStatuses.Processing, OrderStatuses.Shipped, OrderStatuses.Delivered };

        return Render("admin/Analytics", new InertiaProps
        {
            ["range"] = days,
            ["kpis"] = Defer(token => SalesService.KpisAsync(db, since, paidStatuses, token), "kpis")
                .Cache($"admin-analytics:{days}:kpis", TimeSpan.FromSeconds(60)),
            ["series"] = Defer(token => SalesService.SeriesAsync(db, since, token), "charts")
                .Cache($"admin-analytics:{days}:series", TimeSpan.FromSeconds(60)),
            ["topProducts"] = Defer(token => SalesService.TopProductsAsync(db, sinceUtc, paidStatuses, token), "charts")
                .Cache($"admin-analytics:{days}:topProducts", TimeSpan.FromSeconds(60)),
            ["categoryShare"] = Defer(token => SalesService.CategoryShareAsync(db, sinceUtc, paidStatuses, token), "charts")
                .Cache($"admin-analytics:{days}:categoryShare", TimeSpan.FromSeconds(60)),
        });
    }

    // ---------------------------------------------------------------- catalog list + edit

    internal static async Task<IResult> CatalogAsync(string? q, int? page, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        Guid? editId = null;
        if (ctx.Request.Query.TryGetValue("edit", out var edit) && Guid.TryParse(edit, out var parsed))
            editId = parsed;
        var data = await AdminService.GetCatalogAsync(q, page, editId, db, ct);
        return Render("admin/Catalog", new InertiaProps
        {
            ["products"] = data.Products,
            ["q"] = q ?? "",
            ["categories"] = data.Categories,
            ["editing"] = data.Editing,
        });
    }

    internal static async Task<IResult> CreateProductAsync(ProductInput input, HttpContext ctx, AppDb db, JobQueue jobs, CancellationToken ct)
    {
        var result = await AdminService.CreateProductAsync(input, db, jobs, ct);
        return FinishCreated(ctx, result);
    }

    internal static async Task<IResult> EditProductAsync(Guid id, ProductInput input, HttpContext ctx, AppDb db, JobQueue jobs, CancellationToken ct)
    {
        var result = await AdminService.EditProductAsync(id, input, ctx.User.UserIdOrNull(), db, jobs, ct);
        return FinishCreated(ctx, result);
    }

    // Create/edit redirect to /admin/catalog (flash written first); the rest follow the shared Finish mapping.
    static IResult FinishCreated(HttpContext ctx, SaveResult r)
    {
        if (r.RedirectUrl is { } url)
        {
            if (r.FlashType is { } type) ctx.Inertia().Flash("toast", new Toast(type, r.FlashMessage!));
            return Results.Redirect(url);
        }
        return Finish(r);
    }

    internal static async Task<IResult> RestockAsync(Guid id, int delta, string? note, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        Finish(await AdminService.RestockAsync(id, delta, note, ctx.User.UserIdOrNull(), db, realtime, ct));

    // ---------------------------------------------------------------- categories

    internal static async Task<IResult> CreateCategoryAsync(string? name, string? tagline, string? icon, HttpContext ctx, AppDb db, CancellationToken ct) =>
        Finish(await AdminService.CreateCategoryAsync(name, tagline, icon, db, ct));

    internal static async Task<IResult> EditCategoryAsync(Guid id, string? name, string? tagline, string? icon, bool? isActive, HttpContext ctx, AppDb db, CancellationToken ct) =>
        Finish(await AdminService.EditCategoryAsync(id, name, tagline, icon, isActive, db, ct));
}
