namespace VuePgBlog.Features;

public static class Credit
{
    public static void MapCredit(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/account/credit").RequireAuthorization("verified");
        account.MapGet("/", CreditService.AccountAsync);
        account.MapPost("/installments/{id}/pay", CreditService.PayInstallmentAsync);

        var admin = app.MapGroup("/admin/credit").RequireAuthorization("admin");
        admin.MapGet("/", CreditService.AdminAsync);
    }
}
