namespace SvelteMongoBlog.Features.Lots;

public static class Lots
{
    public static void MapLots(this IEndpointRouteBuilder app)
    {
        app.MapGet("/lots", LotsService.IndexAsync);
        app.MapGet("/lots/{id:guid}", LotsService.ShowAsync);
        app.MapGet("/lots/{id:guid}/book", LotsService.BookPageAsync).RequireAuthorization("verified");
        app.MapGet("/", LotsService.HomeAsync);
        app.MapGet("/about", () => LotsService.StaticPage("about"));
        app.MapGet("/policies", () => LotsService.StaticPage("policies"));
    }
}
