using VuePgBlog.Data;
using VuePgBlog.Infrastructure;
namespace VuePgBlog.Features;

public static class Account
{
    public static void MapAccount(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account").RequireAuthorization("verified");
        group.MapGet("/", OverviewAsync);
        group.MapGet("/profile", ProfileAsync);
        group.MapPut("/profile", UpdateProfileAsync).WithInertiaValidation();
        group.MapGet("/preferences", ProfileAsync);
        group.MapPut("/preferences", UpdatePreferencesAsync).WithInertiaValidation();
    }

    static IResult Finish(SaveResult r) => r switch
    {
        { Errors: { } errors } => Back().WithErrors(errors),
        { FlashType: { } type, FlashMessage: { } message } => Back().WithFlash("toast", new Toast(type, message)),
        _ => Back(),
    };

    internal static async Task<IResult> OverviewAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var data = await AccountService.GetOverviewAsync(ctx.User.UserId(), db, ct);
        return Render("account/Index", new InertiaProps
        {
            ["stats"] = data.Stats,
            ["recentOrders"] = data.RecentOrders,
            ["nextInstallment"] = data.NextInstallment,
            ["openDeliveries"] = data.OpenDeliveries,
            ["unreadAlerts"] = data.UnreadAlerts,
        });
    }

    internal static async Task<IResult> ProfileAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var data = await AccountService.GetProfileAsync(ctx.User.UserId(), db, ct);
        return Render("account/Preferences", new InertiaProps
        {
            ["profile"] = data.Profile,
            ["preferences"] = data.Preferences,
        });
    }

    internal static async Task<IResult> UpdateProfileAsync(ProfileInput input, HttpContext ctx, AppDb db, CancellationToken ct) =>
        Finish(await AccountService.SaveProfileAsync(input, ctx.User.UserId(), db, ct));

    internal static async Task<IResult> UpdatePreferencesAsync(PreferencesInput input, HttpContext ctx, AppDb db, CancellationToken ct) =>
        Finish(await AccountService.SavePreferencesAsync(input, ctx, db, ct));
}
