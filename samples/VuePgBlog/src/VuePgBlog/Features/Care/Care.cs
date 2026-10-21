using VuePgBlog.Data;
using VuePgBlog.Infrastructure;
namespace VuePgBlog.Features;

public static class Care
{
    public static void MapCare(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/account").RequireAuthorization("verified");
        var care = account.MapGroup("/care");
        care.MapGet("/warranties", WarrantiesAsync);
        care.MapPost("/warranties/{id}/serial", SetSerialAsync);
        care.MapPost("/warranties/{id}/claims", ClaimWarrantyAsync);
        care.MapGet("/insurance", InsuranceAsync);
        care.MapPost("/insurance/{id}/claims", ClaimInsuranceAsync);
        care.MapPost("/insurance/attach/{orderItemId}", AttachInsuranceAsync);
        care.MapGet("/repairs", RepairsAsync);
        care.MapGet("/repairs/{id}", RepairDetailAsync);
        care.MapPost("/repairs", CreateRepairAsync);
    }

    static IResult Finish(SaveResult r) => r switch
    {
        { NotFound: true } => Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404),
        { Errors: { } errors } => Back().WithErrors(errors),
        { FlashType: { } type, FlashMessage: { } message } => Back().WithFlash("toast", new Toast(type, message)),
        _ => Back(),
    };

    internal static async Task<IResult> WarrantiesAsync(HttpContext ctx, AppDb db, CancellationToken ct) =>
        Render("account/Warranties", new InertiaProps
        {
            ["warranties"] = await CareService.GetWarrantiesAsync(ctx.User.UserId(), db, ct),
        });

    internal static async Task<IResult> SetSerialAsync(Guid id, string? serial, HttpContext ctx, AppDb db, CancellationToken ct) =>
        Finish(await CareService.SetSerialAsync(ctx.User.UserId(), id, serial, db, ct));

    internal static async Task<IResult> ClaimWarrantyAsync(Guid id, string? issue, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct) =>
        Finish(await CareService.ClaimWarrantyAsync(ctx.User.UserId(), id, issue, db, jobs, realtime, ct));

    internal static async Task<IResult> InsuranceAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var data = await CareService.GetInsuranceAsync(ctx.User.UserId(), db, ct);
        return Render("account/Insurance", new InertiaProps
        {
            ["policies"] = data.Policies,
            // Attachable: delivered order items whose product is insurable and not already insured.
            ["attachable"] = data.Attachable,
        });
    }

    internal static async Task<IResult> ClaimInsuranceAsync(Guid id, string? incidentType, string? description, DateOnly? incidentOn, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        Finish(await CareService.ClaimInsuranceAsync(ctx.User.UserId(), id, incidentType, description, incidentOn, db, realtime, ct));

    internal static async Task<IResult> AttachInsuranceAsync(Guid orderItemId, HttpContext ctx, AppDb db, CancellationToken ct) =>
        Finish(await CareService.AttachInsuranceAsync(ctx.User.UserId(), orderItemId, db, ct));

    internal static async Task<IResult> RepairsAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var data = await CareService.GetRepairsAsync(ctx.User.UserId(), db, ct);
        return Render("account/Repairs", new InertiaProps
        {
            ["repairs"] = data.Repairs,
            ["categories"] = data.Categories,
        });
    }

    internal static async Task<IResult> RepairDetailAsync(Guid id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var data = await CareService.GetRepairDetailAsync(ctx.User.UserId(), id, db, ct);
        return data is null
            ? Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404)
            : Render("account/Repairs", new InertiaProps
            {
                ["repair"] = data.Repair,
                ["history"] = data.History,
            });
    }

    internal static async Task<IResult> CreateRepairAsync(string? deviceName, string? problem, string? deviceSerial, Guid? categoryId, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        Finish(await CareService.CreateRepairAsync(ctx.User.UserId(), deviceName, problem, deviceSerial, categoryId, db, realtime, ct));
}

public static class AdminCare
{
    public static void MapAdminCare(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin/care").RequireAuthorization("admin");
        admin.MapGet("/", AdminAsync);
        admin.MapPost("/insurance-claims/{id}", DecideInsuranceClaimAsync);
        admin.MapPost("/warranty-claims/{id}", DecideWarrantyClaimAsync);
        admin.MapPost("/repairs/{id}", UpdateRepairAsync);
    }

    static IResult Finish(SaveResult r) => r switch
    {
        { NotFound: true } => Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404),
        { Errors: { } errors } => Back().WithErrors(errors),
        { FlashType: { } type, FlashMessage: { } message } => Back().WithFlash("toast", new Toast(type, message)),
        _ => Back(),
    };

    internal static async Task<IResult> AdminAsync(string? tab, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var data = await CareService.GetAdminDeskAsync(tab, db, ct);
        return Render("admin/Care", new InertiaProps
        {
            ["tab"] = data.Tab,
            ["warrantyClaims"] = data.WarrantyClaims,
            ["insuranceClaims"] = data.InsuranceClaims,
            ["repairs"] = data.Repairs,
        });
    }

    internal static async Task<IResult> DecideInsuranceClaimAsync(Guid id, string? decision, decimal? payout, string? note, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct) =>
        Finish(await CareService.DecideInsuranceClaimAsync(id, decision, payout, note, db, jobs, realtime, ct));

    internal static async Task<IResult> DecideWarrantyClaimAsync(Guid id, string? decision, string? note, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct) =>
        Finish(await CareService.DecideWarrantyClaimAsync(id, decision, note, db, jobs, realtime, ct));

    internal static async Task<IResult> UpdateRepairAsync(Guid id, string? status, decimal? quotedCost, string? technicianName, DateOnly? appointmentOn, string? note, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct) =>
        Finish(await CareService.UpdateRepairAsync(id, status, quotedCost, technicianName, appointmentOn, note, db, jobs, realtime, ct));
}
