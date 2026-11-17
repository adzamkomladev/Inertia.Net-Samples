using System.Net;
using System.Text;
using static CarParker.Infrastructure.BrandPalette;
using static CarParker.Infrastructure.Display;

namespace CarParker.Infrastructure;

/// <summary>What every template needs: the app name and the public base URL, so every link in every mail is absolute.</summary>
public sealed record EmailContext(string AppName, string BaseUrl)
{
    public string Url(string path) => BaseUrl.TrimEnd('/') + path;
    public string PreferencesUrl => Url("/account/preferences");

    /// <summary>Builds the context from App:BaseUrl. Only Development may fall back to the local address; elsewhere a missing base URL fails the job (it retries).</summary>
    public static EmailContext From(IConfiguration config, IHostEnvironment env) => new(
        config["App:Name"] is { Length: > 0 } name ? name : "CarParker",
        config.AppBaseUrl() ?? (env.IsDevelopment() ? "http://localhost:5041" : throw new InvalidOperationException("App:BaseUrl must be configured to mail links.")));
}

/// <summary>
/// HTML and plain-text bodies in CarParker's look: petrol on cool paper, system sans with the app's fallbacks. One table-based layout
/// (600px, stacks on phones), a hidden preheader, dark-mode colours through a style block (<c>prefers-color-scheme</c> plus the
/// <c>[data-ogsc]</c> hook Outlook apps use), one bulletproof primary button and a plain-text twin for every message.
/// Every interpolated value in HTML goes through <see cref="E"/>.
/// </summary>
public static class Emails
{
    const string Sans = "Archivo,-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    // (selector, declarations): emitted once under prefers-color-scheme and once under [data-ogsc].
    static readonly (string Selector, string Rules)[] Dark =
    [
        (".bg", $"background-color:{DarkPage} !important"),
        (".card", $"background-color:{DarkCard} !important;border-color:{DarkLine} !important"),
        (".ink", $"color:{DarkInk} !important"),
        (".muted", $"color:{DarkMuted} !important"),
        (".line", $"border-color:{DarkLine} !important"),
        (".brand", $"color:{DarkBrand} !important"),
        (".btn", $"background-color:{DarkBrand} !important"),
        (".btn a", $"color:{DarkOnBrand} !important"),
        (".mark", $"background-color:{DarkBrand} !important"),
        (".mark span", $"color:{DarkOnBrand} !important"),
        (".well", $"background-color:{DarkWell} !important"),
        (".ok", $"color:{DarkOk} !important"),
        (".bad", $"color:{DarkBad} !important"),
        (".p-ok", $"background-color:{DarkOkSoft} !important;color:{DarkOk} !important"),
        (".p-bad", $"background-color:{DarkBadSoft} !important;color:{DarkBad} !important"),
        (".p-info", $"background-color:{DarkBrandSoft} !important;color:{DarkBrand} !important"),
    ];

    static string E(object? text) => WebUtility.HtmlEncode(text?.ToString() ?? "");

    // ------------------------------------------------------------ building blocks

    static string DarkCss(string prefix) => string.Concat(Dark.Select(d => $"{prefix}{d.Selector}{{{d.Rules}}}"));

    static string Layout(EmailContext c, string preheader, string body)
    {
        var prefs = $"<a href=\"{E(c.PreferencesUrl)}\" class=\"muted\" style=\"color:{Muted};text-decoration:underline\">Email preferences</a>";
        return $$"""
            <!doctype html>
            <html lang="en" xmlns="http://www.w3.org/1999/xhtml"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <meta http-equiv="Content-Type" content="text/html; charset=UTF-8"><meta name="x-apple-disable-message-reformatting">
            <meta name="color-scheme" content="light dark"><meta name="supported-color-schemes" content="light dark">
            <title>{{E(c.AppName)}}</title>
            <style>
              :root{color-scheme:light dark;supported-color-schemes:light dark}
              body{margin:0;padding:0;-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%}
              table{border-collapse:collapse}
              a{text-decoration:none}
              @media (max-width:620px){
                .outer{padding:16px 0 !important} .card{padding:26px 20px !important;border-radius:0 !important;border-left:0 !important;border-right:0 !important}
                .edge{padding-left:20px !important;padding-right:20px !important} .h1{font-size:23px !important}
                .btnt{width:100% !important} .btn a{display:block !important;text-align:center !important;padding:16px 20px !important}
              }
              @media (prefers-color-scheme:dark){{{DarkCss("")}}}
              {{DarkCss("[data-ogsc] ")}}
            </style></head>
            <body class="bg" style="margin:0;padding:0;background-color:{{Page}}">
            <div style="display:none;max-height:0;overflow:hidden;mso-hide:all;font-size:1px;line-height:1px;color:{{Page}}">{{E(preheader)}}{{string.Concat(Enumerable.Repeat("&#847;&zwnj;&nbsp;", 40))}}</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="bg" bgcolor="{{Page}}" style="background-color:{{Page}}"><tr><td align="center" class="outer" style="padding:32px 12px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:600px">
            <tr><td class="edge" style="padding:0 8px 18px"><table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
              <td class="mark" width="30" height="30" align="center" valign="middle" bgcolor="{{Brand}}" style="width:30px;height:30px;border-radius:8px;background-color:{{Brand}}"><span style="font:700 17px/30px {{Sans}};color:{{OnBrand}}">{{Mark}}</span></td>
              <td style="padding-left:10px"><span class="ink" style="font:700 18px/1 {{Sans}};letter-spacing:-0.01em;color:{{Ink}}">{{E(c.AppName)}}</span></td>
            </tr></table></td></tr>
            <tr><td class="card" style="background-color:{{Card}};border:1px solid {{Line}};border-radius:16px;padding:36px 40px">{{body}}</td></tr>
            <tr><td class="edge muted" style="padding:22px 8px 0;font:12px/1.65 {{Sans}};color:{{Muted}}">You are receiving this because of activity on your {{E(c.AppName)}} account.<br>{{prefs}}</td></tr>
            </table></td></tr></table></body></html>
            """;
    }

    static string Eyebrow(string text, string cls = "brand", string color = Brand) =>
        $"""<p class="{cls}" style="margin:0 0 8px;font:600 14px/1.3 {Sans};color:{color}">{E(text)}</p>""";

    static string Heading(string text) =>
        $"""<h1 class="ink h1" style="margin:0 0 14px;font:700 27px/1.2 {Sans};letter-spacing:-0.02em;color:{Ink}">{E(text)}</h1>""";

    /// <summary>Body paragraph; <paramref name="html"/> must already be escaped.</summary>
    static string P(string html) => $"""<p class="ink" style="margin:0 0 16px;font:400 15.5px/1.65 {Sans};color:{Ink}">{html}</p>""";

    static string Small(string html) => $"""<p class="muted" style="margin:18px 0 0;font:400 13px/1.6 {Sans};color:{Muted}">{html}</p>""";

    static string Button(string url, string label) => $"""
        <table role="presentation" cellpadding="0" cellspacing="0" border="0" class="btnt" style="margin:26px 0 6px"><tr>
        <td class="btn" align="center" bgcolor="{Brand}" style="border-radius:10px;background-color:{Brand}"><a href="{E(url)}" style="display:inline-block;padding:14px 28px;min-height:20px;font:600 15px/20px {Sans};color:{OnBrand};text-decoration:none;border-radius:10px">{E(label)}</a></td>
        </tr></table>
        """;

    static string TextLink(string url, string label) =>
        $"""<p style="margin:14px 0 0;font:500 14px/1.5 {Sans}"><a href="{E(url)}" class="brand" style="color:{Brand};text-decoration:underline">{E(label)}</a></p>""";

    static string Pill(string text, string tone)
    {
        var (cls, fore, back) = tone switch
        {
            "ok" => ("p-ok", Ok, OkSoft),
            "bad" => ("p-bad", Bad, BadSoft),
            _ => ("p-info", Brand, BrandSoft),
        };
        return $"""<span class="{cls}" style="display:inline-block;padding:3px 9px;border-radius:999px;background-color:{back};font:600 12px/16px {Sans};color:{fore}">{E(text)}</span>""";
    }

    /// <summary>Label/value rows inside a quiet well. Values must already be escaped.</summary>
    static string Details(params (string Label, string Html)[] rows)
    {
        var body = string.Concat(rows.Select((r, i) => $"""
            <tr><td class="muted line" style="padding:9px 0;{(i > 0 ? $"border-top:1px solid {Line};" : "")}font:400 13.5px/1.4 {Sans};color:{Muted}" valign="top">{E(r.Label)}</td>
            <td class="ink line" align="right" style="padding:9px 0 9px 16px;{(i > 0 ? $"border-top:1px solid {Line};" : "")}font:600 14px/1.4 {Sans};color:{Ink}" valign="top">{r.Html}</td></tr>
            """));
        return $"""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="well" bgcolor="{Well}" style="margin:6px 0 4px;background-color:{Well};border-radius:12px"><tr><td style="padding:6px 18px"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">{body}</table></td></tr></table>""";
    }

    /// <summary>A note in a quiet well: tonal surface, no side stripe.</summary>
    static string Note(string html, string label) => $"""
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:6px 0 4px"><tr>
        <td class="well" bgcolor="{Well}" style="background-color:{Well};border-radius:12px;padding:16px 20px">
        <p class="muted" style="margin:0 0 6px;font:600 13px/1.3 {Sans};color:{Muted}">{E(label)}</p>
        <p class="ink" style="margin:0;font:400 15px/1.65 {Sans};color:{Ink}">{html}</p></td></tr></table>
        """;

    static string Multiline(string? text) => E(text).Replace("\r\n", "\n").Replace("\n", "<br>");

    static string Plain(params string?[] parts) => string.Join("\n", parts.Where(p => p is not null)) + "\n";

    static string PlainFooter(EmailContext c) =>
        $"\n--\nYou are receiving this because of activity on your {c.AppName} account.\nEmail preferences: {c.PreferencesUrl}\n";

    // ------------------------------------------------------------ templates

    public static EmailMessage Verify(EmailContext c, string to, string name, string url) => new(to, $"Confirm your email for {c.AppName}",
        Layout(c, "One tap and you can start booking parking spots.", $"""
            {Eyebrow("Welcome")}{Heading("Confirm your email")}
            {P($"Hi {E(First(name))}, confirm your email address to start booking parking spots.")}
            {Button(url, "Verify email")}
            {Small($"""This link expires in 24 hours. Button not working? Paste this into your browser:<br><a href="{E(url)}" class="brand" style="color:{Brand};word-break:break-all">{E(url)}</a>""")}
            {Small("Did not sign up? Ignore this email. Nothing happens until the link is used.")}
            """),
        Plain($"Hi {First(name)},", "", "Confirm your email address to start booking parking spots:", url, "", "The link expires in 24 hours. Did not sign up? Ignore this email.") + PlainFooter(c));

    public static EmailMessage SessionEnded(EmailContext c, string to, string name, string lotName, DateTime endedUtc, string bookingUrl, string receiptUrl)
    {
        var ended = DateTimeUtc(endedUtc);
        return new(to, $"Parking session ended at {lotName}",
            Layout(c, $"Your session at {lotName} ended {ended}.", $"""
                {Eyebrow("Session ended", "ok", Ok)}{Heading("Thanks for parking with us")}
                {P($"Hi {E(First(name))}, your parking session just ended and the spot is free again.")}
                {Details(("Lot", E(lotName)), ("Ended", E(ended)))}
                {Button(bookingUrl, "Open your booking")}
                {TextLink(receiptUrl, "Download receipt (PDF)")}
                """),
            Plain($"Hi {First(name)},", "", $"Thanks for parking with us. Your session at {lotName} ended {ended}.", "", $"Open your booking: {bookingUrl}", $"Download receipt (PDF): {receiptUrl}") + PlainFooter(c));
    }

    public static EmailMessage ReservationReminder(EmailContext c, string to, string name, string lotName, string spotLabel, DateTime startUtc, string bookingUrl)
    {
        var start = DateTimeUtc(startUtc);
        return new(to, $"Parking reminder for {lotName}",
            Layout(c, $"Spot {spotLabel} at {lotName} opens {start}.", $"""
                {Eyebrow("Coming up")}{Heading("Your parking spot opens soon")}
                {P($"Hi {E(First(name))}, your spot is ready for you shortly.")}
                {Details(("Lot", E(lotName)), ("Spot", E(spotLabel)), ("Starts", E(start)))}
                {Button(bookingUrl, "Open your booking")}
                """),
            Plain($"Hi {First(name)},", "", $"Spot {spotLabel} at {lotName} opens {start}.", "", $"Open your booking: {bookingUrl}") + PlainFooter(c));
    }

    public static EmailMessage RefundPaid(EmailContext c, string to, string name, long amountCents, Guid reservationId, string paymentsUrl, string refundReceiptUrl)
    {
        var amount = Money(amountCents);
        var booking = ShortId(reservationId);
        return new(to, $"Your {amount} refund has been paid",
            Layout(c, $"{amount} is on its way back to your original payment method. It can take a few business days to show.", $"""
                {Eyebrow("Refund paid", "ok", Ok)}{Heading($"{amount} refunded")}
                {P($"Hi {E(First(name))}, we paid your refund back to the original payment method.")}
                {Details(("Booking", E(booking)), ("Refunded", E(amount)))}
                {Small("Card and mobile money providers decide when the money shows on your statement.")}
                {Button(paymentsUrl, "See your payments")}
                {TextLink(refundReceiptUrl, "Download refund receipt (PDF)")}
                """),
            Plain($"Hi {First(name)},", "", $"Your refund of {amount} for booking {booking} has been paid to the original payment method.", "It can take a few business days to show on your statement.", "", $"See your payments: {paymentsUrl}", $"Download refund receipt (PDF): {refundReceiptUrl}") + PlainFooter(c));
    }

    public static EmailMessage ClaimDecision(EmailContext c, string to, string name, string kind, bool approved, long? payoutCents, string note, string url)
    {
        var headline = approved ? "Your Spot Cover claim was approved" : "Your Spot Cover claim was rejected";
        var payout = payoutCents is { } cents ? Money(cents) : null;
        return new(to, headline,
            Layout(c, payout is null ? $"Your {kind} claim has been reviewed." : $"Your {kind} claim was approved for {payout}.", $"""
                {Eyebrow("Claim reviewed", approved ? "ok" : "bad", approved ? Ok : Bad)}{Heading(headline)}
                {P($"Hi {E(First(name))}, we reviewed your {E(kind)} claim.")}
                {Details(("Claim", E(kind)), ("Decision", Pill(approved ? "Approved" : "Rejected", approved ? "ok" : "bad")), ("Payout", payout is null ? "None" : E(payout)))}
                {(note.Length > 0 ? Note(Multiline(note), "Note from the team") : "")}
                {Button(url, "Open your insurance")}
                """),
            Plain($"Hi {First(name)},", "", $"Your {kind} claim was {(approved ? "approved" : "rejected")}." + (payout is null ? "" : $" Payout: {payout}."), note.Length > 0 ? $"Note: {note}" : null, "", $"Open your insurance: {url}") + PlainFooter(c));
    }
}
