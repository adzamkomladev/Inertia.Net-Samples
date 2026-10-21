namespace VuePgBlog.Features;

public static class Account
{
    public static void MapAccount(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account").RequireAuthorization("verified");
        group.MapGet("/", AccountService.OverviewAsync);
        group.MapGet("/profile", AccountService.ProfileAsync);
        group.MapPut("/profile", AccountService.UpdateProfileAsync).WithInertiaValidation();
        group.MapGet("/preferences", AccountService.ProfileAsync);
        group.MapPut("/preferences", AccountService.UpdatePreferencesAsync).WithInertiaValidation();
    }
}
