namespace KitchenSink.Features.Showcase;

public static class ErrorsFeature
{
    public static IEndpointRouteBuilder MapErrors(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/features/errors").RequireAuthorization();

        group.MapGet("/http-exceptions", () => Inertia.Net.Inertia.Render("Features/Errors/HttpExceptions"));
        group.MapGet("/http-exceptions/403", () => Results.StatusCode(StatusCodes.Status403Forbidden));
        group.MapGet("/http-exceptions/404", () => Results.StatusCode(StatusCodes.Status404NotFound));
        group.MapGet("/http-exceptions/500", () => Results.StatusCode(StatusCodes.Status500InternalServerError));
        group.MapGet("/http-exceptions/unhandled", () => Results.Text("I'm a teapot", statusCode: StatusCodes.Status418ImATeapot));
        group.MapGet("/network-errors", () => Inertia.Net.Inertia.Render("Features/Errors/NetworkErrors"));

        return endpoints;
    }
}
