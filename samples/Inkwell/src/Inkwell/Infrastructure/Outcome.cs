using Inkwell.Infrastructure;

namespace Inkwell.Infrastructure;

/// <summary>What a service action wants the route handler to tell the browser, expressed as plain data so
/// services stay free of Inertia composition. Exactly one branch applies: Errors (back + field errors),
/// Flash (back + flash), Flash + Redirect (flash, then a redirect), Redirect alone, BackFallback (back with a
/// fallback URL), NotFound (404 Error page) or Forbidden (403). Handlers map these onto Render/Back/Results in the route files.</summary>
public sealed record Outcome(
    IDictionary<string, string>? Errors = null,
    (string Key, string Message)? Flash = null,
    string? Redirect = null,
    string? BackFallback = null,
    bool NotFound = false,
    bool Forbidden = false)
{
    /// <summary>One field validation failure, shown back on the form the user came from.</summary>
    public static Outcome Fail(string field, string message) =>
        new(Errors: new Dictionary<string, string> { [field] = message });

    /// <summary>Several field validation failures, shown back on the form the user came from.</summary>
    public static Outcome Fail(IDictionary<string, string> errors) => new(Errors: errors);

    /// <summary>Flash toast and redirect back to the previous page.</summary>
    public static Outcome FlashBack(string key, string message) => new(Flash: (key, message));

    /// <summary>Flash toast first, then send the browser to <paramref name="url"/>.</summary>
    public static Outcome FlashThenRedirect(string key, string message, string url) =>
        new(Flash: (key, message), Redirect: url);

    /// <summary>Flash toast and go back, falling back to <paramref name="fallback"/> when there is no previous page.</summary>
    public static Outcome FlashBackTo(string fallback, string key, string message) =>
        new(Flash: (key, message), BackFallback: fallback);

    public static Outcome RedirectTo(string url) => new(Redirect: url);

    public static Outcome Missing() => new(NotFound: true);

    public static Outcome Denied() => new(Forbidden: true);

    public static Outcome Ok() => new();
}

public static class OutcomeExtensions
{
    /// <summary>The single place a service Outcome becomes Inertia responses (Back/Flash/Redirect/Error pages).</summary>
    public static IResult Respond(this Outcome outcome, HttpContext ctx)
    {
        if (outcome.Errors is { } errors)
            return Back().WithErrors(errors);
        if (outcome.NotFound)
            return ErrorPage.NotFound();
        if (outcome.Forbidden)
            return ErrorPage.Forbidden();
        if (outcome.Flash is { } flash)
            ctx.Inertia().Flash(flash.Key, flash.Message);
        if (outcome.Redirect is { } url)
            return Results.Redirect(url);
        if (outcome.BackFallback is not null)
            return Back(outcome.BackFallback);
        return Back();
    }
}
