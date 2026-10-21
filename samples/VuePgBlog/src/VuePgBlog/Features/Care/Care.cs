namespace VuePgBlog.Features;

public static class Care
{
    public static void MapCare(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/account").RequireAuthorization("verified");
        var care = account.MapGroup("/care");
        care.MapGet("/warranties", CareService.WarrantiesAsync);
        care.MapPost("/warranties/{id}/serial", CareService.SetSerialAsync);
        care.MapPost("/warranties/{id}/claims", CareService.ClaimWarrantyAsync);
        care.MapGet("/insurance", CareService.InsuranceAsync);
        care.MapPost("/insurance/{id}/claims", CareService.ClaimInsuranceAsync);
        care.MapPost("/insurance/attach/{orderItemId}", CareService.AttachInsuranceAsync);
        care.MapGet("/repairs", CareService.RepairsAsync);
        care.MapGet("/repairs/{id}", CareService.RepairDetailAsync);
        care.MapPost("/repairs", CareService.CreateRepairAsync);
    }
}

public static class AdminCare
{
    public static void MapAdminCare(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin/care").RequireAuthorization("admin");
        admin.MapGet("/", CareService.AdminAsync);
        admin.MapPost("/insurance-claims/{id}", CareService.DecideInsuranceClaimAsync);
        admin.MapPost("/warranty-claims/{id}", CareService.DecideWarrantyClaimAsync);
        admin.MapPost("/repairs/{id}", CareService.UpdateRepairAsync);
    }
}
