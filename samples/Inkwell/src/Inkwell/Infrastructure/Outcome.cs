namespace Inkwell.Infrastructure;

/// <summary>What a service action wants the browser to see, as plain data so services stay free of Inertia results.
/// <see cref="OutcomeExtensions.Respond"/> maps it to one response: errors, 404, 403, flash, redirect or back.</summary>
public sealed record Outcome(
    IDictionary<string, string>? Errors = null,
    (string Key, string Message)? Flash = null,
    string? Redirect = null,
    string? BackFallback = null,
    bool NotFound = false,
    bool Forbidden = false)
{
    public static Outcome Fail(string field, string message) => new(Errors: new Dictionary<string, string> { [field] = message });
    public static Outcome Fail(IDictionary<string, string> errors) => new(Errors: errors);
    public static Outcome FlashBack(string key, string message) => new(Flash: (key, message));
    public static Outcome FlashThenRedirect(string key, string message, string url) => new(Flash: (key, message), Redirect: url);
    public static Outcome FlashBackTo(string fallback, string key, string message) => new(Flash: (key, message), BackFallback: fallback);
    public static Outcome RedirectTo(string url) => new(Redirect: url);
    public static Outcome Missing() => new(NotFound: true);
    public static Outcome Denied() => new(Forbidden: true);
    public static Outcome Ok() => new();
}

public static class OutcomeExtensions
{
    public static IResult Respond(this Outcome outcome, HttpContext ctx)
    {
        if (outcome.Errors is { } errors) return Back().WithErrors(errors);
        if (outcome.NotFound) return ErrorPage.NotFound();
        if (outcome.Forbidden) return ErrorPage.Forbidden();
        if (outcome.Flash is { } flash) ctx.Inertia().Flash(flash.Key, flash.Message);
        if (outcome.Redirect is { } url) return Results.Redirect(url);
        return outcome.BackFallback is not null ? Back(outcome.BackFallback) : Back();
    }
}
