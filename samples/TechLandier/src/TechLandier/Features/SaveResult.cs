using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// Plain service outcome. Feature route filters turn it into an HTTP/Inertia response.
public sealed record SaveResult(
    Dictionary<string, string>? Errors = null,
    string? FlashType = null,
    string? FlashMessage = null,
    string? RedirectUrl = null,
    bool NotFound = false)
{
    public static readonly SaveResult Missing = new(NotFound: true);
    public static readonly SaveResult Done = new();

    public static SaveResult Success(string message, string? redirect = null) => new(FlashType: "success", FlashMessage: message, RedirectUrl: redirect);
    public static SaveResult Info(string message, string? redirect = null) => new(FlashType: "info", FlashMessage: message, RedirectUrl: redirect);
    public static SaveResult Error(string message, string? redirect = null) => new(FlashType: "error", FlashMessage: message, RedirectUrl: redirect);
    public static SaveResult Invalid(string field, string message) => new(Errors: new() { [field] = message });

}
