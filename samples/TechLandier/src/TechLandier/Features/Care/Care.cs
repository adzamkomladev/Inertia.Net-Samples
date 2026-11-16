using System.Security.Claims;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Care
{
    public static void MapCare(this IEndpointRouteBuilder app)
    {
        var care = app.MapGroup("/account/care").RequireAuthorization("verified");
        care.MapGet("/warranties", async (ClaimsPrincipal user, CareService svc, CancellationToken ct) =>
            Render("account/Warranties", new InertiaProps { ["warranties"] = await svc.WarrantiesAsync(user.UserId(), ct) })).WithName("account.warranties");
        care.MapPost("/warranties/{id:guid}/serial", async (Guid id, SerialInput input, ClaimsPrincipal user, CareService svc, CancellationToken ct) =>
            await svc.SetSerialAsync(user.UserId(), id, input, ct)).WithName("account.warranties.serial");
        care.MapPost("/warranties/{id:guid}/claims", async (Guid id, WarrantyClaimInput input, ClaimsPrincipal user, CareService svc, CancellationToken ct) =>
            await svc.ClaimWarrantyAsync(user.UserId(), id, input, ct)).WithName("account.warranties.claims.store");

        care.MapGet("/insurance", async (ClaimsPrincipal user, CareService svc, CancellationToken ct) =>
        {
            var data = await svc.InsuranceAsync(user.UserId(), ct);
            return Render("account/Insurance", new InertiaProps { ["policies"] = data.Policies, ["attachable"] = data.Attachable });
        }).WithName("account.insurance");
        care.MapPost("/insurance/{id:guid}/claims", async (Guid id, InsuranceClaimInput input, ClaimsPrincipal user, CareService svc, CancellationToken ct) =>
            await svc.ClaimInsuranceAsync(user.UserId(), id, input, ct)).WithName("account.insurance.claims.store");
        care.MapPost("/insurance/attach/{orderItemId:guid}", async (Guid orderItemId, ClaimsPrincipal user, CareService svc, CancellationToken ct) =>
            await svc.AttachInsuranceAsync(user.UserId(), orderItemId, ct)).WithName("account.insurance.attach");

        care.MapGet("/repairs", async (ClaimsPrincipal user, CareService svc, CancellationToken ct) =>
        {
            var data = await svc.RepairsAsync(user.UserId(), ct);
            return Render("account/Repairs", new InertiaProps { ["repairs"] = data.Repairs, ["categories"] = data.Categories });
        }).WithName("account.repairs");
        care.MapGet("/repairs/{id:guid}", RepairDetail).WithName("account.repairs.show");
        care.MapPost("/repairs", async (RepairInput input, ClaimsPrincipal user, CareService svc, CancellationToken ct) =>
            await svc.CreateRepairAsync(user.UserId(), input, ct)).WithName("account.repairs.store");

        var admin = app.MapGroup("/admin/care").RequireAuthorization("admin");
        admin.MapGet("/", AdminDesk).WithName("admin.care");
        admin.MapGet("/{kind}/{id:guid}", async (string kind, Guid id, CareService svc, CancellationToken ct) =>
            await svc.AdminDetailAsync(kind, id, ct) is { } detail
                ? Render("admin/CareDetail", new InertiaProps { ["detail"] = detail })
                : HttpExtensions.NotFoundPage()).WithName("admin.care.show");
        admin.MapPost("/insurance-claims/{id:guid}", async (Guid id, InsuranceDecisionInput input, CareService svc, CancellationToken ct) =>
            await svc.DecideInsuranceClaimAsync(id, input, ct)).WithName("admin.care.insurance-claims.decide");
        admin.MapPost("/warranty-claims/{id:guid}", async (Guid id, WarrantyDecisionInput input, CareService svc, CancellationToken ct) =>
            await svc.DecideWarrantyClaimAsync(id, input, ct)).WithName("admin.care.warranty-claims.decide");
        admin.MapPost("/repairs/{id:guid}", async (Guid id, RepairUpdateInput input, CareService svc, CancellationToken ct) =>
            await svc.UpdateRepairAsync(id, input, ct)).WithName("admin.care.repairs.update");
    }

    static async Task<IResult> RepairDetail(Guid id, ClaimsPrincipal user, CareService svc, CancellationToken ct)
    {
        if (await svc.RepairDetailAsync(user.UserId(), id, ct) is not { } data) return HttpExtensions.NotFoundPage();
        return Render("account/Repairs", new InertiaProps { ["repair"] = data.Repair, ["history"] = data.History, ["repairs"] = Array.Empty<RepairListRow>(), ["categories"] = Array.Empty<CategoryOption>() });
    }

    // Only the open tab's list is queried; the others stay empty.
    static async Task<IResult> AdminDesk(string? tab, CareService svc, CancellationToken ct)
    {
        tab = tab is "insurance" or "repairs" ? tab : "warranties";
        return Render("admin/Care", new InertiaProps
        {
            ["tab"] = tab,
            ["warrantyClaims"] = tab == "warranties" ? await svc.WarrantyClaimsAsync(ct) : Array.Empty<WarrantyClaimAdminRow>(),
            ["insuranceClaims"] = tab == "insurance" ? await svc.InsuranceClaimsAsync(ct) : Array.Empty<InsuranceClaimAdminRow>(),
            ["repairs"] = tab == "repairs" ? await svc.RepairQueueAsync(ct) : Array.Empty<RepairAdminRow>(),
        });
    }
}
