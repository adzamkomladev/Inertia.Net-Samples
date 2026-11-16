using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;

namespace Inkwell.Infrastructure;

/// <summary>Unsubscribe pages for optional mail, and (Development only) a gallery that renders every template with sample data.</summary>
public static class EmailEndpoints
{
    public static void MapEmailEndpoints(this WebApplication app)
    {
        // GET only asks: link scanners and previews fetch URLs, so the preference changes on POST.
        app.MapGet("/email/unsubscribe", (string? token, EmailUnsubscribe unsubscribe, IOptions<AppOptions> options) =>
            unsubscribe.Read(token) is { } named
                ? Page(options.Value.Name, "Unsubscribe?", $"""
                    <p>Stop sending you {E(EmailUnsubscribe.Describe(named.Kind))}? Everything else stays as it is.</p>
                    <form method="post"><button type="submit">Unsubscribe</button></form>
                    <p class="muted">You can turn it back on any time in <a href="{E(unsubscribe.SettingsUrl)}">settings</a>.</p>
                    """)
                : Invalid(options.Value.Name, unsubscribe.SettingsUrl))
            .AllowAnonymous();

        // The page's button and the mail client's RFC 8058 one-click POST both land here; the signed token is the authority.
        app.MapPost("/email/unsubscribe", async (string? token, EmailUnsubscribe unsubscribe, IOptions<AppOptions> options, CancellationToken ct) =>
                await unsubscribe.ApplyAsync(token, ct) is { } kind
                    ? Page(options.Value.Name, "You're unsubscribed", $"""
                        <p>We won't send you {E(EmailUnsubscribe.Describe(kind))} any more.</p>
                        <p class="muted">Changed your mind? Turn it back on in <a href="{E(unsubscribe.SettingsUrl)}">settings</a>.</p>
                        """)
                    : Invalid(options.Value.Name, unsubscribe.SettingsUrl))
            .AllowAnonymous().DisableAntiforgery();

        // Daily digests sent before the shared endpoint link here.
        app.MapGet("/email/unsubscribe/daily", (string? token) =>
                Results.Redirect($"/email/unsubscribe?token={Uri.EscapeDataString(token ?? "")}"))
            .AllowAnonymous();

        if (app.Environment.IsDevelopment()) MapPreviews(app);
    }

    static string E(string s) => HtmlEncoder.Default.Encode(s);

    static IResult Invalid(string appName, string settingsUrl) => Page(appName, "This link no longer works", $"""
        <p>Unsubscribe links expire after 60 days. You can still change every email in <a href="{E(settingsUrl)}">settings</a>.</p>
        """, StatusCodes.Status400BadRequest);

    static IResult Page(string appName, string heading, string body, int status = StatusCodes.Status200OK) => Results.Content($$"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="color-scheme" content="light dark"><meta name="robots" content="noindex"><title>{{E(heading)}} · {{E(appName)}}</title>
        <style>
          :root{--paper:#f6f2ea;--card:#fffdf9;--ink:#2a241f;--muted:#6e655c;--line:#e8e1d5;--brand:#a64a22;--on-brand:#fff}
          @media (prefers-color-scheme:dark){:root{--paper:#1b1714;--card:#241f1b;--ink:#efe9df;--muted:#b0a698;--line:#3a332c;--brand:#e59a6e;--on-brand:#1b1714} }
          *{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;padding:24px 16px;background:var(--paper);color:var(--ink);font:16px/1.6 -apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif}
          main{width:100%;max-width:440px}.mark{font:600 24px/1 Newsreader,Georgia,serif;letter-spacing:-.02em;margin:0 0 18px 4px}
          .card{background:var(--card);border:1px solid var(--line);border-radius:18px;padding:32px 28px}
          h1{margin:0 0 12px;font:600 26px/1.2 Newsreader,Georgia,serif;letter-spacing:-.01em}p{margin:0 0 14px}.muted{color:var(--muted);font-size:14px;margin:18px 0 0}
          a{color:var(--brand)}button{min-height:44px;padding:0 24px;border:0;border-radius:999px;background:var(--brand);color:var(--on-brand);font:600 15px/1 inherit;cursor:pointer}
          button:focus-visible,a:focus-visible{outline:2px solid var(--brand);outline-offset:3px}
        </style></head>
        <body><main><p class="mark">{{E(appName)}}</p><div class="card"><h1>{{E(heading)}}</h1>{{body}}</div></main></body></html>
        """, "text/html; charset=utf-8", statusCode: status);

    static void MapPreviews(WebApplication app)
    {
        var samples = new Dictionary<string, Func<string, EmailContent>>
        {
            ["verification"] = name => EmailTemplates.Verification(name, "Ada Lovelace", "http://localhost/email/verify/sample", Footer(null)),
            ["comment"] = name => EmailTemplates.NewComment(name, "Ada Lovelace", "Grace Hopper", "Notes on quiet craft",
                "This is the clearest case for slow work I've read in years. The part about compounding attention stayed with me all day.",
                "http://localhost/posts/sample#comments", Footer(EmailKind.Comments)),
            ["likes"] = name => EmailTemplates.NewLikes(name, "Ada Lovelace", ["Grace Hopper", "Alan Turing"], 7, "Notes on quiet craft",
                "http://localhost/posts/sample", Footer(EmailKind.Likes)),
            ["weekly"] = name => EmailTemplates.WeeklyDigest(name, "Ada Lovelace",
            [
                new("Notes on quiet craft", "http://localhost/posts/1", 1284, 96, 14),
                new("The garden in November", "http://localhost/posts/2", 412, 23, 5),
                new("Why I stopped outlining", "http://localhost/posts/3", 87, 4, 1),
            ], Footer(EmailKind.Weekly)),
            ["daily"] = name => EmailTemplates.Recommendations(name, "Ada Lovelace",
            [
                ("The long middle of a novel", "Every draft I've finished went through a stretch where nothing seemed to move. Here's what I do now.", "http://localhost/posts/4"),
                ("Field notes from a slow kitchen", "Bread, patience and the pleasure of a recipe that refuses to be rushed.", "http://localhost/posts/5"),
            ], Footer(EmailKind.Daily)),
        };

        app.MapGet("/dev/emails", () => Results.Content(
            "<!doctype html><meta charset=utf-8><title>Email previews</title><body style=\"font:16px/1.8 system-ui;padding:24px\"><h1>Email previews</h1><ul>"
            + string.Concat(samples.Keys.Select(k => $"<li><a href=\"/dev/emails/{k}\">{k}</a> · <a href=\"/dev/emails/{k}?format=text\">text</a></li>"))
            + "</ul></body>", "text/html; charset=utf-8")).AllowAnonymous();

        app.MapGet("/dev/emails/{name}", (string name, string? format, IOptions<AppOptions> options) =>
            samples.TryGetValue(name, out var render) && render(options.Value.Name) is var m
                ? format == "text"
                    ? Results.Text($"Subject: {m.Subject}\n\n{m.Plain}", "text/plain; charset=utf-8")
                    : Results.Content(m.Html, "text/html; charset=utf-8")
                : Results.NotFound()).AllowAnonymous();
    }

    static EmailFooter Footer(EmailKind? kind) => new("http://localhost/settings",
        kind is null ? null : "http://localhost/email/unsubscribe?token=sample",
        kind is { } k ? EmailUnsubscribe.Reason(k) : null);
}
