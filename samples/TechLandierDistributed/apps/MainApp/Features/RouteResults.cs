namespace TechLandier.Features;

/// <summary>HTTP/Inertia-only results that belong at the route boundary, not in the domain library.</summary>
public static class RouteResults
{
    public static IResult NotFoundPage() => Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);

    /// Not Results.Forbid(): that goes through the cookie handler's AccessDeniedPath.
    public static IResult Forbidden() => Results.StatusCode(StatusCodes.Status403Forbidden);
}
