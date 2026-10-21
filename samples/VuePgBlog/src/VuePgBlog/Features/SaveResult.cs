namespace VuePgBlog.Features;

/// <summary>
/// Plain outcome of a domain command that can fail. Services return this; the route handler turns it into
/// Inertia responses (Back().WithErrors / Back().WithFlash / redirect). All fields null = plain Back().
/// </summary>
public sealed record SaveResult(
    Dictionary<string, string>? Errors = null,
    string? FlashType = null,
    string? FlashMessage = null,
    string? RedirectUrl = null,
    bool NotFound = false);
