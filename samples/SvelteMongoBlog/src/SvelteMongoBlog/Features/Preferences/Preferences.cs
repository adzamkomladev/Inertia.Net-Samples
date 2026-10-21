namespace SvelteMongoBlog.Features;

public static class Preferences
{
    public static void MapPreferences(this IEndpointRouteBuilder app)
    {
        app.MapGet("/account/profile", (Delegate)PreferencesService.ShowAsync).RequireAuthorization("verified");
        app.MapPut("/account/profile", PreferencesService.UpdateProfileAsync).RequireAuthorization("verified").WithInertiaValidation();
        app.MapGet("/account/preferences", (Delegate)PreferencesService.ShowAsync).RequireAuthorization("verified");
        app.MapPut("/account/preferences", PreferencesService.UpdatePreferencesAsync).RequireAuthorization("verified").WithInertiaValidation();
    }
}
