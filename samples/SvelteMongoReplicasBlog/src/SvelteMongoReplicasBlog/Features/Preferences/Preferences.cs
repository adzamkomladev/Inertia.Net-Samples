namespace SvelteMongoReplicasBlog.Features;

public static class Preferences
{
    public static void MapPreferences(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/settings").RequireAuthorization();
        group.MapGet("/", () => Results.Redirect("/settings/preferences"));
        group.MapGet("/preferences", PreferencesService.ShowPreferencesAsync);
        group
            .MapPut("/preferences", PreferencesService.UpdatePreferencesAsync)
            .WithInertiaValidation();
        group.MapPut("/profile", PreferencesService.UpdateProfileAsync).WithInertiaValidation();
    }
}
