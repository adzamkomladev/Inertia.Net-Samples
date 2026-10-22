using System.Security.Claims;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Account
{
    public static void MapAccount(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account").RequireAuthorization("verified");
        group.MapGet("/", Overview);
        group.MapGet("/profile", Profile);
        group.MapGet("/preferences", Profile);
        group.MapPut("/profile", (ProfileInput input, ClaimsPrincipal user, AccountService svc, CancellationToken ct) =>
            svc.SaveProfileAsync(user.UserId(), input, ct)).WithInertiaValidation();
        group.MapPut("/preferences", SavePreferences).WithInertiaValidation();
    }

    // Independent widgets, each on its own context, so they load together.
    static IResult Overview(ClaimsPrincipal user, AccountService svc)
    {
        var uid = user.UserId();
        return Render("account/Index", new InertiaProps
        {
            ["stats"] = Prop(ct => svc.StatsAsync(uid, ct)).Parallel(),
            ["recentOrders"] = Prop(ct => svc.RecentOrdersAsync(uid, ct)).Parallel(),
            ["nextInstallment"] = Prop(ct => svc.NextInstallmentAsync(uid, ct)).Parallel(),
            ["openDeliveries"] = Prop(ct => svc.OpenDeliveriesAsync(uid, ct)).Parallel(),
            ["unreadAlerts"] = Prop(ct => svc.UnreadAlertsAsync(uid, ct)).Parallel(),
        });
    }

    static async Task<IResult> Profile(ClaimsPrincipal user, AccountService svc, CancellationToken ct)
    {
        var (profile, preferences) = await svc.ProfileAsync(user.UserId(), ct);
        return Render("account/Preferences", new InertiaProps { ["profile"] = profile, ["preferences"] = preferences });
    }

    static async Task<SaveResult> SavePreferences(PreferencesInput input, ClaimsPrincipal user, HttpContext ctx, AccountService svc, CancellationToken ct)
    {
        var result = await svc.SavePreferencesAsync(user.UserId(), input, ct);
        // Read by the inline script in app.html before first paint, so SSR pages do not flash the wrong theme.
        if (result.Errors is null)
            ctx.Response.Cookies.Append("theme", input.Theme ?? "system",
                new CookieOptions { MaxAge = TimeSpan.FromDays(365), SameSite = SameSiteMode.Lax, Path = "/" });
        return result;
    }
}
