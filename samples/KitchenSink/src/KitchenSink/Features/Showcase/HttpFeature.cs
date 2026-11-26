using KitchenSink.Infrastructure;

namespace KitchenSink.Features.Showcase;

public static class HttpFeature
{
    public static IEndpointRouteBuilder MapHttp(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/features/http").RequireAuthorization();

        group.MapGet("/use-http", () => Inertia.Net.Inertia.Render("Features/Http/UseHttp"));
        group.MapPost("/use-http/api", async (HttpContext context, CancellationToken cancellationToken) =>
        {
            var input = await RequestInput.ReadAsync(context.Request, cancellationToken);
            var name = input.String("name") ?? "World";

            return Results.Json(new
            {
                message = $"Hello, {name}!",
                timestamp = DateTime.UtcNow.ToString("O"),
            });
        });

        return endpoints;
    }
}
