namespace ReactSqliteBlog.Features.Analytics;

public static class AnalyticsFeature
{
    public static IServiceCollection AddAnalyticsFeature(this IServiceCollection services)
    {
        services.AddScoped<ViewTracker>();
        return services;
    }

    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/analytics", AnalyticsService.Overview).RequireAuthorization();
        app.MapGet("/posts/{id:guid}/analytics", AnalyticsService.PostAnalyticsAsync)
            .RequireAuthorization();
        return app;
    }
}
