namespace ReactSqliteBlog.Features.Settings;

public static class SettingsFeature
{
    public static IServiceCollection AddSettingsFeature(this IServiceCollection services) =>
        services;

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/settings").RequireAuthorization();
        g.MapGet("", SettingsService.ShowAsync);
        g.MapPut("/profile", SettingsService.UpdateProfileAsync).WithInertiaValidation();
        g.MapPut("/preferences", SettingsService.UpdatePreferencesAsync).WithInertiaValidation();
        return app;
    }
}
