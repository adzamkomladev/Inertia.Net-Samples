using System.Globalization;
using System.Net;
using System.Text;
using TechLandier.Features;

namespace TechLandier.Infrastructure;

/// <summary>What every template needs: the store identity, the public base URL and, for preference-backed mail, the signed unsubscribe link.</summary>
public sealed record EmailContext(StoreInfo Store, string BaseUrl, string? UnsubscribeUrl = null)
{
    public string Url(string path) => BaseUrl.TrimEnd('/') + path;
    public string PreferencesUrl => Url("/account/preferences");
}

/// <summary>How a message explains itself at the bottom: transactional mail only links the settings; notices also carry the reason and the unsubscribe link.</summary>
enum FooterKind { Account, Notice, Signup }

/// <summary>
/// HTML and plain-text bodies in TechLandier's look: neutral surfaces, one blue accent, Geist/Inter with system fallbacks.
/// One table-based layout (600px, stacks on phones), a hidden preheader, dark-mode colours through a style block
/// (<c>prefers-color-scheme</c> plus the <c>[data-ogsc]</c> hook Outlook apps use), a bulletproof button and a plain-text
/// twin for every message. Every interpolated value in HTML goes through <see cref="E"/>.
/// </summary>
public static class Emails
{
    const string Page = "#f4f6f8", Card = "#ffffff", Ink = "#12161b", Muted = "#595e64", Line = "#e1e3e5", Soft = "#eef2fa", Well = "#f4f6f8";
    const string Brand = "#1957d2", Ok = "#0b7643", Bad = "#c2181d", Warn = "#975800";
    const string Sans = "Geist,Inter,-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    // (selector, declarations): emitted once under prefers-color-scheme and once under [data-ogsc].
    static readonly (string Selector, string Rules)[] Dark =
    [
        (".bg", "background-color:#131417 !important"),
        (".card", "background-color:#1b1d21 !important;border-color:#2b2e32 !important"),
        (".ink", "color:#eceff1 !important"),
        (".muted", "color:#a0a5ab !important"),
        (".line", "border-color:#2b2e32 !important"),
        (".brand", "color:#669bff !important"),
        (".btn", "background-color:#669bff !important"),
        (".btn a", "color:#070b14 !important"),
        (".mark", "background-color:#669bff !important"),
        (".mark span", "color:#070b14 !important"),
        (".soft", "background-color:#202b3f !important"),
        (".well", "background-color:#222428 !important"),
        (".ok", "color:#55c483 !important"),
        (".bad", "color:#fa686a !important"),
        (".warn", "color:#eeb154 !important"),
        (".p-ok", "background-color:#15291f !important;color:#55c483 !important"),
        (".p-bad", "background-color:#331a1b !important;color:#fa686a !important"),
        (".p-info", "background-color:#202b3f !important;color:#669bff !important"),
        (".p-quiet", "background-color:#222428 !important;color:#a0a5ab !important"),
        (".fill", "background-color:#669bff !important"),
        (".track", "background-color:#2b2e32 !important"),
    ];

    static string E(object? text) => WebUtility.HtmlEncode(text?.ToString() ?? "");
    static string Money(decimal amount) => Pricing.Show(amount);
    static string Day(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    static string Day(StoreInfo store, DateTime utc) => store.Local(utc).ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    static string First(string? name) => string.IsNullOrWhiteSpace(name) ? "there" : name.Trim().Split(' ', 2)[0];
    static string Status(string status) => status.Replace('_', ' ');

    // ------------------------------------------------------------ building blocks

    static string DarkCss(string prefix) => string.Concat(Dark.Select(d => $"{prefix}{d.Selector}{{{d.Rules}}}"));

    static string Layout(EmailContext c, string preheader, string body, FooterKind kind, string? reason = null)
    {
        var store = c.Store;
        var footer = kind switch
        {
            FooterKind.Signup => $"You are receiving this because this address was used to create a {E(store.Name)} account.",
            FooterKind.Notice => $"{E(reason)}<br><a href=\"{E(c.PreferencesUrl)}\" class=\"muted\" style=\"color:{Muted};text-decoration:underline\">Email preferences</a>"
                + (c.UnsubscribeUrl is null ? "" : $" &nbsp;·&nbsp; <a href=\"{E(c.UnsubscribeUrl)}\" class=\"muted\" style=\"color:{Muted};text-decoration:underline\">Unsubscribe</a>"),
            _ => $"You are receiving this because of activity on your {E(store.Name)} account.<br><a href=\"{E(c.PreferencesUrl)}\" class=\"muted\" style=\"color:{Muted};text-decoration:underline\">Email preferences</a>",
        };
        var help = store.Email.Length == 0 ? "" : $"""Questions? Write to <a href="mailto:{E(store.Email)}" class="brand" style="color:{Brand};text-decoration:underline">{E(store.Email)}</a>.<br>""";
        var address = string.Join(" · ", new[] { store.Name, store.Address }.Where(s => s.Length > 0).Select(E));
        return $$"""
            <!doctype html>
            <html lang="en" xmlns="http://www.w3.org/1999/xhtml"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <meta http-equiv="Content-Type" content="text/html; charset=UTF-8"><meta name="x-apple-disable-message-reformatting">
            <meta name="color-scheme" content="light dark"><meta name="supported-color-schemes" content="light dark">
            <title>{{E(store.Name)}}</title>
            <style>
              :root{color-scheme:light dark;supported-color-schemes:light dark}
              body{margin:0;padding:0;-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%}
              table{border-collapse:collapse}
              a{text-decoration:none}
              @media (max-width:620px){
                .outer{padding:16px 0 !important} .card{padding:26px 20px !important;border-radius:0 !important;border-left:0 !important;border-right:0 !important}
                .edge{padding-left:20px !important;padding-right:20px !important} .h1{font-size:23px !important}
                .stack{display:block !important;width:100% !important;padding:0 0 18px !important} .btnt{width:100% !important} .btn a{display:block !important;text-align:center !important;padding:16px 20px !important}
              }
              @media (prefers-color-scheme:dark){{{DarkCss("")}}}
              {{DarkCss("[data-ogsc] ")}}
            </style></head>
            <body class="bg" style="margin:0;padding:0;background-color:{{Page}}">
            <div style="display:none;max-height:0;overflow:hidden;mso-hide:all;font-size:1px;line-height:1px;color:{{Page}}">{{E(preheader)}}{{string.Concat(Enumerable.Repeat("&#847;&zwnj;&nbsp;", 40))}}</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="bg" bgcolor="{{Page}}" style="background-color:{{Page}}"><tr><td align="center" class="outer" style="padding:32px 12px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:600px">
            <tr><td class="edge" style="padding:0 8px 18px"><table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
              <td class="mark" width="30" height="30" align="center" valign="middle" bgcolor="{{Brand}}" style="width:30px;height:30px;border-radius:8px;background-color:{{Brand}}"><span style="font:700 17px/30px {{Sans}};color:#ffffff">T</span></td>
              <td style="padding-left:10px"><span class="ink" style="font:700 18px/1 {{Sans}};letter-spacing:-0.01em;color:{{Ink}}">{{E(store.Name)}}</span></td>
            </tr></table></td></tr>
            <tr><td class="card" style="background-color:{{Card}};border:1px solid {{Line}};border-radius:16px;padding:36px 40px">{{body}}</td></tr>
            <tr><td class="edge muted" style="padding:22px 8px 0;font:12px/1.65 {{Sans}};color:{{Muted}}">{{help}}{{footer}}<br><span style="color:{{Muted}}">{{address}}</span></td></tr>
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
        <td class="btn" align="center" bgcolor="{Brand}" style="border-radius:10px;background-color:{Brand}"><a href="{E(url)}" style="display:inline-block;padding:14px 28px;min-height:20px;font:600 15px/20px {Sans};color:#ffffff;text-decoration:none;border-radius:10px">{E(label)}</a></td>
        </tr></table>
        """;

    static string TextLink(string url, string label) =>
        $"""<p style="margin:14px 0 0;font:500 14px/1.5 {Sans}"><a href="{E(url)}" class="brand" style="color:{Brand};text-decoration:underline">{E(label)}</a></p>""";

    static string Pill(string text, string tone)
    {
        var (cls, fore, back) = tone switch
        {
            "ok" => ("p-ok", Ok, "#e3f3ea"),
            "bad" => ("p-bad", Bad, "#fbe6e6"),
            "quiet" => ("p-quiet", Muted, "#eceef1"),
            _ => ("p-info", Brand, "#e4ecf9"),
        };
        return $"""<span class="{cls}" style="display:inline-block;padding:3px 9px;border-radius:999px;background-color:{back};font:600 12px/16px {Sans};color:{fore}">{E(text)}</span>""";
    }

    static string StatusPill(string status) => Pill(Status(status), status switch
    {
        InstallmentStatuses.Paid => "ok",
        InstallmentStatuses.Overdue => "bad",
        InstallmentStatuses.Waived => "quiet",
        _ => "info",
    });

    /// <summary>Label/value rows inside a quiet well. Values must already be escaped.</summary>
    static string Details(params (string Label, string Html)[] rows)
    {
        var body = string.Concat(rows.Select((r, i) => $"""
            <tr><td class="muted line" style="padding:9px 0;{(i > 0 ? $"border-top:1px solid {Line};" : "")}font:400 13.5px/1.4 {Sans};color:{Muted}" valign="top">{E(r.Label)}</td>
            <td class="ink line" align="right" style="padding:9px 0 9px 16px;{(i > 0 ? $"border-top:1px solid {Line};" : "")}font:600 14px/1.4 {Sans};color:{Ink}" valign="top">{r.Html}</td></tr>
            """));
        return $"""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="well" bgcolor="{Well}" style="margin:6px 0 4px;background-color:{Well};border-radius:12px"><tr><td style="padding:6px 18px"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">{body}</table></td></tr></table>""";
    }

    static string Quote(string html, string? label = null) => $"""
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:6px 0 4px"><tr>
        <td class="soft" bgcolor="{Soft}" style="background-color:{Soft};border-radius:12px;padding:16px 20px">
        {(label is null ? "" : $"""<p class="muted" style="margin:0 0 6px;font:600 13px/1.3 {Sans};color:{Muted}">{E(label)}</p>""")}
        <p class="ink" style="margin:0;font:400 15px/1.65 {Sans};color:{Ink}">{html}</p></td></tr></table>
        """;

    static string Multiline(string? text) => E(text).Replace("\r\n", "\n").Replace("\n", "<br>");

    static string Section(string label) =>
        $"""<p class="ink" style="margin:24px 0 6px;font:600 14px/1.3 {Sans};color:{Ink}">{E(label)}</p>""";

    static string Progress(int done, int total)
    {
        var pct = total <= 0 ? 0 : Math.Clamp(done * 100 / total, 0, 100);
        var filled = pct == 0 ? "" : $"""<td class="fill" width="{pct}%" height="8" bgcolor="{Brand}" style="width:{pct}%;height:8px;background-color:{Brand};font-size:0;line-height:0;border-radius:4px">&nbsp;</td>""";
        return $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:14px 0 4px"><tr><td>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="track" bgcolor="{Line}" style="background-color:{Line};border-radius:4px"><tr>{filled}<td style="font-size:0;line-height:0;height:8px">&nbsp;</td></tr></table></td></tr>
            <tr><td class="muted" style="padding-top:6px;font:400 12.5px/1.4 {Sans};color:{Muted}">{done} of {total} installments paid</td></tr></table>
            """;
    }

    static string Lines(ReceiptData r) =>
        $"""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:6px 0 0">{string.Concat(r.Lines.Select(l => $"""
            <tr><td class="line" style="padding:12px 0;border-bottom:1px solid {Line}" valign="top">
            <div class="ink" style="font:600 14.5px/1.4 {Sans};color:{Ink}">{E(l.Name)}</div>
            <div class="muted" style="margin-top:2px;font:400 12.5px/1.4 {Sans};color:{Muted}">Qty {l.Qty} · {Money(l.UnitPrice)} each</div></td>
            <td class="line ink" align="right" valign="top" style="padding:12px 0 12px 16px;border-bottom:1px solid {Line};font:600 14.5px/1.4 {Sans};color:{Ink};white-space:nowrap">{Money(l.Total)}</td></tr>
            """))}</table>""";

    static string Totals(ReceiptData r)
    {
        string Row(string label, string value, string extra = "") =>
            $"""<tr><td class="muted" style="padding:4px 0;font:400 14px/1.4 {Sans};color:{Muted}">{label}</td><td align="right" class="ink" style="padding:4px 0;font:500 14px/1.4 {Sans};color:{Ink};{extra}">{value}</td></tr>""";
        return $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:10px 0 0">
            {Row("Subtotal", Money(r.Subtotal))}
            {(r.Discount > 0 ? Row("Discount", $"<span class=\"ok\" style=\"color:{Ok}\">&minus;{Money(r.Discount)}</span>") : "")}
            {Row("Shipping", r.Shipping > 0 ? Money(r.Shipping) : "Free")}
            <tr><td class="ink line" style="padding:12px 0 0;border-top:1px solid {Line};font:700 16px/1.3 {Sans};color:{Ink}">Total</td>
            <td align="right" class="ink line" style="padding:12px 0 0;border-top:1px solid {Line};font:700 20px/1.3 {Sans};color:{Ink}">{Money(r.Total)}</td></tr></table>
            """;
    }

    static string Plain(params string?[] parts) => string.Join("\n", parts.Where(p => p is not null)) + "\n";

    static string PlainFooter(EmailContext c, FooterKind kind, string? reason = null)
    {
        var sb = new StringBuilder("\n--\n");
        sb.Append(kind switch
        {
            FooterKind.Signup => $"You are receiving this because this address was used to create a {c.Store.Name} account.\n",
            FooterKind.Notice => $"{reason}\nEmail preferences: {c.PreferencesUrl}\n" + (c.UnsubscribeUrl is null ? "" : $"Unsubscribe: {c.UnsubscribeUrl}\n"),
            _ => $"You are receiving this because of activity on your {c.Store.Name} account.\nEmail preferences: {c.PreferencesUrl}\n",
        });
        if (c.Store.Email.Length > 0) sb.Append($"Questions? {c.Store.Email}\n");
        sb.Append(string.Join(" · ", new[] { c.Store.Name, c.Store.Address }.Where(s => s.Length > 0)));
        return sb.ToString();
    }

    const string InstallmentReason = "You get installment notices because installment reminders are switched on for this account.";

    // ------------------------------------------------------------ templates

    public static SendEmail Verify(EmailContext c, string to, string? name, string url) => new(to, $"Confirm your email for {c.Store.Name}",
        Layout(c, "One tap and your account is ready for shopping, installment plans and support.", $"""
            {Eyebrow("Welcome")}{Heading("Confirm your email")}
            {P($"Hi {E(First(name))}, one tap and your account is fully unlocked for shopping, installment plans and support.")}
            {Button(url, "Verify email")}
            {Small($"""This link expires in 24 hours. Button not working? Paste this into your browser:<br><a href="{E(url)}" class="brand" style="color:{Brand};word-break:break-all">{E(url)}</a>""")}
            {Small("Did not sign up? Ignore this email. Nothing happens until the link is used.")}
            """, FooterKind.Signup),
        Plain($"Hi {First(name)},", "", "Confirm your email to unlock shopping, installment plans and support:", url, "", "The link expires in 24 hours. Did not sign up? Ignore this email.") + PlainFooter(c, FooterKind.Signup));

    public static SendEmail OrderPaid(EmailContext c, string to, string? name, ReceiptData r, string trackUrl, string receiptUrl)
    {
        var paid = Day(r.Store, r.PaidAt);
        var plan = r.Plan is { } p
            ? Details(("Installment plan", $"{p.Count} × {Money(p.PerInstallment)}"), ("First installment due", Day(p.FirstDue)), ("Plan fee", Money(p.Fee)))
            : "";
        var html = Layout(c, $"Order {r.OrderNumber} confirmed · {Money(r.Total)} {(r.Plan is null ? "paid" : "on installments")} · receipt {r.ReceiptNumber}", $"""
            {Eyebrow("Order confirmed", "ok", Ok)}{Heading($"Thanks, {First(name)}. Your order is in.")}
            {P($"We received your payment on {E(paid)} and are getting your order ready. You will get another email as soon as it ships.")}
            {Details(("Order", E(r.OrderNumber)), ("Receipt", E(r.ReceiptNumber)), ("Paid", E(paid)), ("Payment", E(ReceiptData.MethodLabel(r.PaymentMethod)) + (r.PaymentRef is null ? "" : $"""<br><span class="muted" style="font:400 12px/1.4 {Sans};color:{Muted}">{E(r.PaymentRef)}</span>""")))}
            {Section("Your items")}{Lines(r)}{Totals(r)}
            {(plan.Length > 0 ? Section("Pay later") + plan : "")}
            {Section("Delivering to")}
            <p class="ink" style="margin:0;font:400 14.5px/1.6 {Sans};color:{Ink}"><strong>{E(r.Recipient)}</strong><br>{E(r.Street)}<br><span class="muted" style="color:{Muted}">{E(r.Phone)}</span></p>
            {Button(trackUrl, "Track your order")}
            {TextLink(receiptUrl, "Download receipt (PDF)")}
            """, FooterKind.Account);
        var text = Plain($"Hi {First(name)},", "", $"Order {r.OrderNumber} is confirmed. Payment received on {paid}.", $"Receipt {r.ReceiptNumber} · {ReceiptData.MethodLabel(r.PaymentMethod)}" + (r.PaymentRef is null ? "" : $" ({r.PaymentRef})"), "")
            + string.Join("\n", r.Lines.Select(l => $"  {l.Qty} × {l.Name}  {Money(l.Total)}")) + "\n\n"
            + Plain($"Subtotal: {Money(r.Subtotal)}", r.Discount > 0 ? $"Discount: -{Money(r.Discount)}" : null, $"Shipping: {(r.Shipping > 0 ? Money(r.Shipping) : "Free")}", $"Total: {Money(r.Total)}", "")
            + (r.Plan is { } pl ? Plain($"Installment plan: {pl.Count} × {Money(pl.PerInstallment)} (plan fee {Money(pl.Fee)}), first due {Day(pl.FirstDue)}", "") : "")
            + Plain($"Delivering to: {r.Recipient}, {r.Street}, {r.Phone}", "", $"Track your order: {trackUrl}", $"Download receipt (PDF): {receiptUrl}")
            + PlainFooter(c, FooterKind.Account);
        return new(to, $"Receipt {r.ReceiptNumber} · order {r.OrderNumber}", html, text);
    }

    public static SendEmail OrderShipped(EmailContext c, string to, string? name, string orderNumber, IReadOnlyList<(string Name, int Qty)> items, string? rider, string eta, string url)
    {
        var html = Layout(c, $"Order {orderNumber} left the store · estimated {eta}", $"""
            {Eyebrow("On its way")}{Heading($"Order {orderNumber} has shipped")}
            {P($"Hi {E(First(name))}, your package has left the store{(rider is null ? "" : $" and {E(rider)} is handling the delivery")}.")}
            {(rider is null ? Details(("Estimated arrival", E(eta))) : Details(("Estimated arrival", E(eta)), ("Rider", E(rider))))}
            {Section("In the package")}
            {string.Concat(items.Select(i => $"""<p class="ink" style="margin:0 0 6px;font:400 14.5px/1.5 {Sans};color:{Ink}">{i.Qty} × {E(i.Name)}</p>"""))}
            {Button(url, "Track your delivery")}
            """, FooterKind.Account);
        var text = Plain($"Hi {First(name)},", "", $"Order {orderNumber} has shipped{(rider is null ? "" : $" with {rider}")}. Estimated arrival: {eta}.", "")
            + string.Join("\n", items.Select(i => $"  {i.Qty} × {i.Name}")) + $"\n\nTrack your delivery: {url}" + PlainFooter(c, FooterKind.Account);
        return new(to, $"Order {orderNumber} shipped", html, text);
    }

    public static SendEmail OrderCancelled(EmailContext c, string to, string? name, string orderNumber, string reason, string url)
    {
        var html = Layout(c, $"Order {orderNumber} was cancelled: {reason}", $"""
            {Eyebrow("Order cancelled", "bad", Bad)}{Heading($"Order {orderNumber} was cancelled")}
            {P($"Hi {E(First(name))}, this order will not be shipped.")}
            {Quote(Multiline(reason), "Reason")}
            {P("If you already paid, the refund is on its way and we will confirm it by email.")}
            {Button(url, "View order")}
            """, FooterKind.Account);
        return new(to, $"Order {orderNumber} cancelled", html,
            Plain($"Hi {First(name)},", "", $"Order {orderNumber} was cancelled and will not be shipped.", $"Reason: {reason}", "If you already paid, the refund is on its way and we will confirm it by email.", "", $"View order: {url}") + PlainFooter(c, FooterKind.Account));
    }

    public static SendEmail OrderRefund(EmailContext c, string to, string? name, string orderNumber, decimal amount, string url)
    {
        var html = Layout(c, $"{Money(amount)} refunded for order {orderNumber}. Card statements can take 1-3 business days.", $"""
            {Eyebrow("Refund issued", "ok", Ok)}{Heading($"{Money(amount)} refunded")}
            {P($"Hi {E(First(name))}, your payment for order <strong>{E(orderNumber)}</strong> has been refunded in full.")}
            {Details(("Order", E(orderNumber)), ("Refunded", Money(amount)), ("Arrives", "1–3 business days"))}
            {Small("Card and mobile money providers decide when the money shows on your statement. If it has not arrived after 3 business days, reply to this email.")}
            {Button(url, "View order")}
            """, FooterKind.Account);
        return new(to, $"Refund for order {orderNumber}", html,
            Plain($"Hi {First(name)},", "", $"Your payment for order {orderNumber} was refunded in full: {Money(amount)}.", "It can take 1-3 business days to show on your statement.", "", $"View order: {url}") + PlainFooter(c, FooterKind.Account));
    }

    public static SendEmail InstallmentPaid(EmailContext c, string to, string? name, string orderNumber, int number, int count, int paidCount, decimal amount, decimal remaining, string url, string statementUrl)
    {
        var html = Layout(c, $"{Money(amount)} received on {orderNumber}. {Money(remaining)} remaining.", $"""
            {Eyebrow("Payment received", "ok", Ok)}{Heading($"Installment {number} of {count} received")}
            {P($"Hi {E(First(name))}, thank you. Your installment on order <strong>{E(orderNumber)}</strong> is in.")}
            {Details(("Order", E(orderNumber)), ("Paid now", Money(amount)), ("Remaining", remaining > 0 ? Money(remaining) : "Nothing left to pay"))}
            {Progress(paidCount, count)}
            {Button(url, "Open the plan")}
            {TextLink(statementUrl, "Download statement (PDF)")}
            """, FooterKind.Notice, InstallmentReason);
        return new(to, $"Installment {number}/{count} paid · {orderNumber}", html,
            Plain($"Hi {First(name)},", "", $"Installment {number}/{count} of {Money(amount)} is paid on {orderNumber}; {Money(remaining)} remaining.", "", $"Open the plan: {url}", $"Download statement (PDF): {statementUrl}") + PlainFooter(c, FooterKind.Notice, InstallmentReason), c.UnsubscribeUrl);
    }

    public static SendEmail InstallmentReminder(EmailContext c, string to, string? name, string orderNumber, int number, decimal amount, DateOnly dueDate, string url)
    {
        var due = Day(dueDate);
        var html = Layout(c, $"{Money(amount)} is due on {due} for order {orderNumber}.", $"""
            {Eyebrow("Payment due soon")}{Heading($"{Money(amount)} is due {due}")}
            {P($"Hi {E(First(name))}, your next installment is coming up. Pay early and stay clear of overdue status, which pauses new installment plans.")}
            {Details(("Order", E(orderNumber)), ("Installment", $"#{number}"), ("Amount", Money(amount)), ("Due", E(due)))}
            {Button(url, "Pay installment")}
            """, FooterKind.Notice, InstallmentReason);
        return new(to, $"Installment {number} due {dueDate.ToString("d MMM", CultureInfo.InvariantCulture)} · {orderNumber}", html,
            Plain($"Hi {First(name)},", "", $"Installment {number} of {Money(amount)} on {orderNumber} is due {due}.", "", $"Pay installment: {url}") + PlainFooter(c, FooterKind.Notice, InstallmentReason), c.UnsubscribeUrl);
    }

    public static SendEmail InstallmentOverdue(EmailContext c, string to, string? name, string orderNumber, int number, decimal amount, DateOnly dueDate, string url)
    {
        var due = Day(dueDate);
        var html = Layout(c, $"{Money(amount)} on order {orderNumber} was due {due}. Pay now to unlock new plans.", $"""
            {Eyebrow("Payment overdue", "bad", Bad)}{Heading($"Installment {number} is overdue")}
            {P($"Hi {E(First(name))}, the installment on order <strong>{E(orderNumber)}</strong> was due {E(due)}. Overdue plans pause new installment financing until it is paid.")}
            {Details(("Order", E(orderNumber)), ("Installment", $"#{number}"), ("Amount", $"""<span class="bad" style="color:{Bad}">{Money(amount)}</span>"""), ("Was due", E(due)))}
            {Button(url, "Pay now")}
            """, FooterKind.Notice, InstallmentReason);
        return new(to, $"Overdue: installment {number} on {orderNumber}", html,
            Plain($"Hi {First(name)},", "", $"Installment {number} of {Money(amount)} on {orderNumber} was due {due} and is overdue. New installment plans stay paused until it is paid.", "", $"Pay now: {url}") + PlainFooter(c, FooterKind.Notice, InstallmentReason), c.UnsubscribeUrl);
    }

    /// <summary>One message per user across every active plan; each plan lists its open installments and links its own PDF.</summary>
    public static SendEmail InstallmentStatement(EmailContext c, string to, string? name, IReadOnlyList<StatementData> plans, string url)
    {
        var open = plans.Sum(p => p.Remaining);
        var overdue = plans.Sum(p => p.Overdue);
        var next = plans.Select(p => p.Next).Where(n => n is not null).OrderBy(n => n!.DueDate).FirstOrDefault();
        string PdfUrl(StatementData p) => c.Url($"/account/installments/{p.PlanId}/statement.pdf");
        var blocks = string.Concat(plans.Select(p => $"""
            {Section($"Order {p.OrderNumber} · {p.Rows.Count(r => r.Status == InstallmentStatuses.Paid)} of {p.Count} paid")}
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">{string.Concat(p.Rows.Where(r => r.Status is InstallmentStatuses.Due or InstallmentStatuses.Overdue).Select(r => $"""
                <tr><td class="line ink" style="padding:10px 0;border-bottom:1px solid {Line};font:500 14px/1.4 {Sans};color:{Ink}">#{r.Number} · {E(Day(r.DueDate))}</td>
                <td class="line" align="center" style="padding:10px 8px;border-bottom:1px solid {Line}">{StatusPill(r.Status)}</td>
                <td class="line ink" align="right" style="padding:10px 0;border-bottom:1px solid {Line};font:600 14px/1.4 {Sans};color:{Ink};white-space:nowrap">{Money(r.Amount)}</td></tr>
                """))}</table>
            {TextLink(PdfUrl(p), "Download statement (PDF)")}
            """));
        var html = Layout(c, $"{Money(open)} open across {plans.Count} {(plans.Count == 1 ? "plan" : "plans")}{(next is null ? "" : $" · next due {Day(next.DueDate)}")}", $"""
            {Eyebrow("Weekly statement", overdue > 0 ? "bad" : "brand", overdue > 0 ? Bad : Brand)}{Heading("Your installments this week")}
            {P($"Hi {E(First(name))}, here is what is still open on your active plans.")}
            {Details(("Open balance", Money(open)), ("Overdue", overdue > 0 ? $"""<span class="bad" style="color:{Bad}">{Money(overdue)}</span>""" : Money(0)), ("Next due", next is null ? "—" : $"{E(Day(next.DueDate))} · {Money(next.Amount)}"))}
            {blocks}
            {Button(url, "Manage plans")}
            """, FooterKind.Notice, InstallmentReason);
        var text = Plain($"Hi {First(name)},", "", $"Open balance: {Money(open)}" + (overdue > 0 ? $" (overdue {Money(overdue)})" : ""), "")
            + string.Concat(plans.Select(p => Plain($"Order {p.OrderNumber} ({p.Rows.Count(r => r.Status == InstallmentStatuses.Paid)} of {p.Count} paid)")
                + string.Concat(p.Rows.Where(r => r.Status is InstallmentStatuses.Due or InstallmentStatuses.Overdue).Select(r => $"  #{r.Number}  {Day(r.DueDate)}  {Money(r.Amount)}  {Status(r.Status)}\n"))
                + $"  Download statement (PDF): {PdfUrl(p)}\n\n"))
            + $"Manage plans: {url}" + PlainFooter(c, FooterKind.Notice, InstallmentReason);
        return new(to, $"Your weekly {c.Store.Name} installment statement", html, text, c.UnsubscribeUrl);
    }

    public static SendEmail TicketReply(EmailContext c, string to, string? name, string ticketNumber, string subject, string reply, string url)
    {
        var shown = reply.Length > 2000 ? reply[..2000].TrimEnd() + "…" : reply;
        var html = Layout(c, $"{Truncate(reply, 90)}", $"""
            {Eyebrow("Support reply")}{Heading($"Support replied on {ticketNumber}")}
            {P($"Hi {E(First(name))}, here is the reply to <strong>{E(subject)}</strong>.")}
            {Quote(Multiline(shown), "Support team")}
            {Button(url, "Open the thread")}
            {Small("Open the thread to answer. Replies there reach the same support agent.")}
            """, FooterKind.Account);
        return new(to, $"Support replied · {ticketNumber}", html,
            Plain($"Hi {First(name)},", "", $"Support replied on {ticketNumber} ({subject}):", "", shown, "", $"Open the thread: {url}") + PlainFooter(c, FooterKind.Account));
    }

    public static SendEmail CareDecision(EmailContext c, string to, string? name, string kind, string item, string decision, string? note, string url)
    {
        var title = char.ToUpperInvariant(kind[0]) + kind[1..];
        var html = Layout(c, $"{title} for {item}: {decision}", $"""
            {Eyebrow(title)}{Heading($"Update on your {kind}")}
            {P($"Hi {E(First(name))}, here is where things stand for your <strong>{E(item)}</strong>.")}
            {Details((title, E(item)), ("Status", $"""<span class="brand" style="color:{Brand}">{E(decision)}</span>"""))}
            {(note is { Length: > 0 } ? Section("Note from the team") + Quote(Multiline(note)) : "")}
            {Button(url, "Open your care dashboard")}
            """, FooterKind.Account);
        return new(to, $"Your {kind} update", html,
            Plain($"Hi {First(name)},", "", $"{title} for {item}: {decision}", note is { Length: > 0 } ? $"\nNote: {note}" : null, "", $"Open your care dashboard: {url}") + PlainFooter(c, FooterKind.Account));
    }

    static string Truncate(string text, int length) => text.Length <= length ? text : text[..length].TrimEnd() + "…";
}
