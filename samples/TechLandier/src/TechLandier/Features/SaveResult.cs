using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// Outcome of a command that can fail. Services return it and the route handler hands it straight back:
/// it renders itself as a 404 page, a redirect, or back-with-errors/flash.
public sealed record SaveResult(
    Dictionary<string, string>? Errors = null,
    string? FlashType = null,
    string? FlashMessage = null,
    string? RedirectUrl = null,
    bool NotFound = false) : IResult
{
    public static readonly SaveResult Missing = new(NotFound: true);
    public static readonly SaveResult Done = new();

    public static SaveResult Success(string message, string? redirect = null) => new(FlashType: "success", FlashMessage: message, RedirectUrl: redirect);
    public static SaveResult Info(string message, string? redirect = null) => new(FlashType: "info", FlashMessage: message, RedirectUrl: redirect);
    public static SaveResult Error(string message, string? redirect = null) => new(FlashType: "error", FlashMessage: message, RedirectUrl: redirect);
    public static SaveResult Invalid(string field, string message) => new(Errors: new() { [field] = message });

    public Task ExecuteAsync(HttpContext ctx)
    {
        if (NotFound) return HttpExtensions.NotFoundPage().ExecuteAsync(ctx);
        var toast = FlashType is null ? null : new Toast(FlashType, FlashMessage!);
        if (RedirectUrl is { } url)
        {
            if (toast is not null) ctx.Inertia().Flash("toast", toast);
            return Results.Redirect(url).ExecuteAsync(ctx);
        }
        var back = Back();
        if (Errors is not null) back.WithErrors(Errors);
        else if (toast is not null) back.WithFlash("toast", toast);
        return back.ExecuteAsync(ctx);
    }
}
