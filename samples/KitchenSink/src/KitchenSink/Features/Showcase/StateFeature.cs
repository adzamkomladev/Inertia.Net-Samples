namespace KitchenSink.Features.Showcase;

public static class StateFeature
{
    public static IEndpointRouteBuilder MapState(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/features/state").RequireAuthorization();

        group.MapGet("/remember", () => Inertia.Net.Inertia.Render("Features/State/Remember"));
        group.MapGet("/flash-data", () => Inertia.Net.Inertia.Render("Features/State/FlashData"));
        group.MapPost("/flash-data", () =>
            Inertia.Net.Inertia.Back()
                .WithFlash("message", "This is a flash message from the server!")
                .WithFlash("type", "success"));
        group.MapPost("/flash-data/error", () =>
            Inertia.Net.Inertia.Back()
                .WithFlash("message", "Something went wrong!")
                .WithFlash("type", "error"));
        group.MapPost("/flash-data/warning", () =>
            Inertia.Net.Inertia.Back()
                .WithFlash("message", "Please check your input.")
                .WithFlash("type", "warning"));
        group.MapGet("/shared-props", () => Inertia.Net.Inertia.Render("Features/State/SharedProps"));

        return endpoints;
    }
}
