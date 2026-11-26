namespace KitchenSink.Features.Showcase;

public static class EventsFeature
{
    public static IEndpointRouteBuilder MapEvents(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/features/events").RequireAuthorization();

        group.MapGet("/global-events", () => Inertia.Net.Inertia.Render("Features/Events/GlobalEvents"));
        group.MapPost("/global-events/action", () =>
            Inertia.Net.Inertia.Back().WithFlash("message", "Action completed successfully!"));

        group.MapGet("/once-events", () => Inertia.Net.Inertia.Render("Features/Events/OnceEvents"));
        group.MapPost("/once-events/action", () =>
            Inertia.Net.Inertia.Back().WithFlash("message", "One-shot action fired!"));

        group.MapGet("/visit-callbacks", () => Inertia.Net.Inertia.Render("Features/Events/VisitCallbacks"));
        group.MapPost("/visit-callbacks/action", () =>
            Inertia.Net.Inertia.Back().WithFlash("message", "Visit callback action completed!"));

        group.MapGet("/progress", () => Inertia.Net.Inertia.Render("Features/Events/Progress"));
        group.MapGet("/progress/slow", async (CancellationToken cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return Inertia.Net.Inertia.Render("Features/Events/Progress");
        });

        group.MapGet("/location-event", () => Inertia.Net.Inertia.Render("Features/Events/LocationEvent"));
        // VersionResolver assigns this path a new asset version, so the middleware answers a client with a
        // 409 X-Inertia-Location (versionChange) before the handler runs. Non-Inertia requests just go back.
        group.MapGet("/location-event/deploy", () => Inertia.Net.Inertia.Back());

        return endpoints;
    }
}
