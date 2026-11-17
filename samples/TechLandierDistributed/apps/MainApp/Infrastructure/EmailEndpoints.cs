using System.Net;
using Microsoft.AspNetCore.Mvc;
using TechLandier.Features;

namespace TechLandier.Infrastructure;

/// <summary>Sample data for every template: the Development preview gallery and the tests render from it.</summary>
public static class EmailSamples
{
    /// <param name="probe">Appended to every free-text value, so a test can prove each one is escaped.</param>
    public static IReadOnlyDictionary<string, SendEmail> All(string probe = "")
    {
        const string to = "ada@example.com";
        var c = new EmailContext(StoreInfo.Sample, "http://localhost", "http://localhost/email/unsubscribe?token=sample");
        var name = probe + " Lovelace";
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        ReceiptData Receipt(ReceiptPlan? plan) => new(StoreInfo.Sample, "TL-2026-00142", "RCP-2026-000142", now.AddMinutes(-4), now,
            name, to, "Ada Lovelace" + probe, "+233 24 123 4567", "GA-183-5287 Nyaneba Avenue, East Legon" + probe,
            [
                new("Aurora X1 Smartphone 256GB" + probe, "AUR-1F3A9C20", 1, 899.00m, 899.00m),
                new("Wavelength Pro ANC Headphones", "WAV-7B21D4E8", 2, 119.50m, 239.00m),
                new("USB-C Fast Charger 65W", "VLT-90C4A512", 1, 39.99m, 39.99m),
            ],
            1177.99m, 58.90m, 12.00m, 1131.09m, plan is null ? PaymentMethods.Card : PaymentMethods.MobileMoney, "simulated-638987654321", plan);

        StatementData Plan(string order, int count, decimal fee, params string[] statuses) => new(StoreInfo.Sample, Guid.Parse("018f3a52-0000-7000-8000-000000000001"), order,
            name, to, PayPlanStatuses.Active, now.AddDays(-30), null, count, 392.37m, fee,
            statuses.Select((s, i) => new StatementRow(i + 1, 392.37m, today.AddDays(14 * (i - 1) + 3), s,
                s == InstallmentStatuses.Paid ? now.AddDays(-14 * (statuses.Length - i)) : null)).ToList());

        var plan = Plan("TL-2026-00142", 3, 53.86m, InstallmentStatuses.Paid, InstallmentStatuses.Due, InstallmentStatuses.Due);
        var late = Plan("TL-2026-00119", 6, 112.40m, InstallmentStatuses.Paid, InstallmentStatuses.Overdue, InstallmentStatuses.Due, InstallmentStatuses.Due, InstallmentStatuses.Due, InstallmentStatuses.Due);

        return new Dictionary<string, SendEmail>
        {
            ["verify"] = Emails.Verify(c with { UnsubscribeUrl = null }, to, name, "http://localhost/email/verify/confirm?token=sample"),
            ["order-paid"] = Emails.OrderPaid(c, to, name, Receipt(null), "http://localhost/orders/TL-2026-00142?t=sample", "http://localhost/orders/TL-2026-00142/receipt.pdf?t=sample"),
            ["order-paid-installments"] = Emails.OrderPaid(c, to, name, Receipt(new ReceiptPlan(3, 392.37m, 53.86m, today.AddDays(14))), "http://localhost/orders/TL-2026-00142?t=sample", "http://localhost/orders/TL-2026-00142/receipt.pdf?t=sample"),
            ["order-shipped"] = Emails.OrderShipped(c, to, name, "TL-2026-00142", [("Aurora X1 Smartphone 256GB" + probe, 1), ("Wavelength Pro ANC Headphones", 2)], "Kwame Boateng" + probe, "Same day if ordered by 2pm", "http://localhost/orders/TL-2026-00142?t=sample"),
            ["order-cancelled"] = Emails.OrderCancelled(c, to, name, "TL-2026-00142", "Out of stock at the warehouse." + probe, "http://localhost/account/orders/TL-2026-00142"),
            ["order-refund"] = Emails.OrderRefund(c, to, name, "TL-2026-00142", 1131.09m, "http://localhost/account/orders/TL-2026-00142"),
            ["installment-paid"] = Emails.InstallmentPaid(c, to, name, "TL-2026-00142", 1, 3, 1, 392.37m, 784.74m, "http://localhost/account/credit", "http://localhost/account/installments/sample/statement.pdf"),
            ["installment-reminder"] = Emails.InstallmentReminder(c, to, name, "TL-2026-00142", 2, 392.37m, today.AddDays(3), "http://localhost/account/credit"),
            ["installment-overdue"] = Emails.InstallmentOverdue(c, to, name, "TL-2026-00119", 2, 392.37m, today.AddDays(-4), "http://localhost/account/credit"),
            ["installment-statement"] = Emails.InstallmentStatement(c, to, name, [plan, late], "http://localhost/account/credit"),
            ["ticket-reply"] = Emails.TicketReply(c, to, name, "T-0042", "Screen flickers after update" + probe,
                "Thanks for flagging this, Ada.\nWe have reproduced the flicker on the 256GB model and a firmware fix ships Thursday. Until then, turning off adaptive brightness avoids it." + probe,
                "http://localhost/account/support"),
            ["care-decision"] = Emails.CareDecision(c, to, name, "warranty claim", "Aurora X1 Smartphone" + probe, "approved — replacement coming" + probe,
                "Please drop the old unit at the Osu counter when your replacement arrives." + probe, "http://localhost/account/care"),
        };
    }
}

/// <summary>One-click unsubscribe pages and, in Development only, the template gallery.</summary>
public static class EmailEndpoints
{
    public static void MapEmailEndpoints(this WebApplication app)
    {
        // GET only asks: link scanners and previews fetch URLs, so the preference changes on POST.
        app.MapGet("/email/unsubscribe", (string? token, [FromServices] EmailUnsubscribe unsubscribe, [FromServices] IConfiguration config) =>
            unsubscribe.Read(token) is { } named
                ? Page("Unsubscribe?", $"""
                    <p>Stop sending you {E(EmailUnsubscribe.Describe(named.Kind))}? Order receipts and delivery updates are not affected.</p>
                    <form method="post"><button type="submit">Unsubscribe</button></form>
                    <p class="muted">You can turn them back on any time in <a href="{E(config.PublicBaseUrl())}/account/preferences">email preferences</a>.</p>
                    """)
                : Invalid(config)).AllowAnonymous();

        // The page's button and the mail client's RFC 8058 one-click POST both land here; the signed token is the authority.
        app.MapPost("/email/unsubscribe", async (string? token, [FromServices] EmailUnsubscribe unsubscribe, [FromServices] IConfiguration config, CancellationToken ct) =>
                await unsubscribe.ApplyAsync(token, ct) is { } kind
                    ? Page("You are unsubscribed", $"""
                        <p>We will not send you {E(EmailUnsubscribe.Describe(kind))} any more.</p>
                        <p class="muted">Changed your mind? Turn them back on in <a href="{E(config.PublicBaseUrl())}/account/preferences">email preferences</a>.</p>
                        """)
                    : Invalid(config))
            .AllowAnonymous().DisableAntiforgery();

        if (app.Environment.IsDevelopment()) MapPreviews(app);
    }

    static string E(string s) => WebUtility.HtmlEncode(s);

    static IResult Invalid(IConfiguration config) => Page("This link no longer works", $"""
        <p>Unsubscribe links expire after 60 days. You can still change every email in <a href="{E(config.PublicBaseUrl())}/account/preferences">email preferences</a>.</p>
        """, StatusCodes.Status400BadRequest);

    static IResult Page(string heading, string body, int status = StatusCodes.Status200OK) => Results.Content($$"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="color-scheme" content="light dark"><meta name="robots" content="noindex"><title>{{E(heading)}}</title>
        <style>
          :root{--page:#f4f6f8;--card:#fff;--ink:#12161b;--muted:#595e64;--line:#e1e3e5;--brand:#1957d2;--on-brand:#fff}
          @media (prefers-color-scheme:dark){:root{--page:#131417;--card:#1b1d21;--ink:#eceff1;--muted:#a0a5ab;--line:#2b2e32;--brand:#669bff;--on-brand:#070b14}
          }
          *{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;padding:24px 16px;background:var(--page);color:var(--ink);font:16px/1.6 Geist,Inter,-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif}
          main{width:100%;max-width:440px}.card{background:var(--card);border:1px solid var(--line);border-radius:16px;padding:32px 28px}
          h1{margin:0 0 12px;font-size:24px;line-height:1.2;letter-spacing:-.02em}p{margin:0 0 14px}.muted{color:var(--muted);font-size:14px;margin:18px 0 0}
          a{color:var(--brand)}button{min-height:44px;padding:0 24px;border:0;border-radius:10px;background:var(--brand);color:var(--on-brand);font:600 15px/1 inherit;cursor:pointer}
          button:focus-visible,a:focus-visible{outline:2px solid var(--brand);outline-offset:3px}
        </style></head>
        <body><main><div class="card"><h1>{{E(heading)}}</h1>{{body}}</div></main></body></html>
        """, "text/html; charset=utf-8", statusCode: status);

    static void MapPreviews(WebApplication app)
    {

        app.MapGet("/dev/emails", () => Results.Content(
            "<!doctype html><meta charset=utf-8><title>Email previews</title><body style=\"font:16px/1.9 system-ui;padding:24px\"><h1>Email previews</h1><ul>"
            + string.Concat(EmailSamples.All().Keys.Select(k => $"<li><a href=\"/dev/emails/{k}\">{k}</a> · <a href=\"/dev/emails/{k}?format=text\">text</a></li>"))
            + "</ul></body>", "text/html; charset=utf-8")).AllowAnonymous();

        app.MapGet("/dev/emails/{name}", (string name, string? format) =>
            EmailSamples.All().TryGetValue(name, out var mail)
                ? format == "text"
                    ? Results.Text($"Subject: {mail.Subject}\n\n{mail.Text}", "text/plain; charset=utf-8")
                    : Results.Content(mail.Html, "text/html; charset=utf-8")
                : Results.NotFound()).AllowAnonymous();
    }
}
