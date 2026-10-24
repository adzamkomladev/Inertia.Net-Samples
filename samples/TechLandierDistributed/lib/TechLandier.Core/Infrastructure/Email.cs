using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TechLandier.Infrastructure;

/// Maileroo HTTP API (https://maileroo.com/docs). Without an API key emails are only logged in Development; elsewhere sending throws.
public sealed class Mailer(HttpClient http, IConfiguration config, IHostEnvironment env, ILogger<Mailer> log)
{
    public async Task<string?> SendAsync(string to, string subject, string html, string text, CancellationToken ct)
    {
        // Seed accounts must never reach an external mailbox, even with production credentials.
        if (to.StartsWith("inertia.demo42.", StringComparison.OrdinalIgnoreCase) && to.EndsWith("@yopmail.com", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.Combine(config["App:DataDir"] is { Length: > 0 } data ? data : "storage", "captured-mail");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".txt"), $"To: {to}\nSubject: {subject}\n\n{text}", ct);
            return null;
        }
        var apiKey = config["Maileroo:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            // Outside Development, logging would leak verification links; fail so the job retries / is marked Failed.
            if (!env.IsDevelopment()) throw new InvalidOperationException("Maileroo:ApiKey is not configured; cannot send email.");
            log.LogInformation("Email (not sent, Maileroo:ApiKey empty) to {To}: {Subject}\n{Text}", to, subject, text);
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://smtp.maileroo.com/api/v2/emails")
        {
            Content = JsonContent.Create(new MailerooEmail(
                new(config["Maileroo:From"] ?? throw new InvalidOperationException("Maileroo:From is required."), config["Maileroo:FromName"] ?? config["App:Name"]),
                [new(to, null)], subject, html, text)),
        };
        request.Headers.Add("X-Api-Key", apiKey);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Maileroo returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        return await TryReadProviderReferenceAsync(response, ct);
    }

    static async Task<string?> TryReadProviderReferenceAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = document.RootElement;
            foreach (var container in new[] { root, root.TryGetProperty("data", out var data) ? data : default })
            {
                if (container.ValueKind != JsonValueKind.Object) continue;
                foreach (var key in new[] { "message_id", "messageId", "id", "reference" })
                    if (container.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                        && value.GetString() is { Length: > 0 and <= 256 } reference)
                        return reference;
            }
        }
        catch (JsonException) { }
        catch (Exception) { }
        return null;
    }

    sealed record MailerooEmail(
        [property: JsonPropertyName("from")] MailerooAddress From,
        [property: JsonPropertyName("to")] MailerooAddress[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("plain")] string Plain);

    sealed record MailerooAddress(
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("display_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DisplayName);
}

/// Email bodies as plain functions; no template engine needed for a handful of messages.
public static class Emails
{
    static string Layout(string title, string bodyHtml) => $$"""
        <!doctype html><html><body style="font-family:system-ui,sans-serif;background:#f4f4f5;padding:24px">
        <div style="max-width:560px;margin:auto;background:#fff;border-radius:12px;padding:32px">
        <div style="font-size:13px;letter-spacing:2px;color:#0f0f11;font-weight:700">TECHLANDIER</div>
        <h1 style="font-size:20px;margin:8px 0 16px">{{WebUtility.HtmlEncode(title)}}</h1>{{bodyHtml}}
        <p style="color:#71717a;font-size:12px;margin-top:24px;border-top:1px solid #e4e4e7;padding-top:12px">TechLandier — gadgets you love, pay your way.</p></div></body></html>
        """;

    static string Button(string url, string label) =>
        $"""<p><a href="{WebUtility.HtmlEncode(url)}" style="display:inline-block;background:#0f0f11;color:#fff;padding:10px 18px;border-radius:8px;text-decoration:none">{WebUtility.HtmlEncode(label)}</a></p>""";

    static string Money(decimal amount) => $"${amount:0.00}";
    static string Esc(object? text) => WebUtility.HtmlEncode(text?.ToString() ?? "");

    public static SendEmail Verify(string to, string? name, string url) => new(to, "Verify your TechLandier email",
        Layout("Confirm your email", $"<p>Hi {Esc(name)}, one tap and your account is fully unlocked for shopping, installment plans and support.</p>{Button(url, "Verify email")}<p style=\"color:#666;font-size:13px\">This link expires in 24 hours.</p>"),
        $"Welcome, {name}! Verify your TechLandier email: {url} (expires in 24 hours)");

    public static SendEmail Welcome(string to, string? name, string url) => new(to, "Welcome to TechLandier",
        Layout($"Welcome to TechLandier, {name}!", "<p>Your account is live — explore deals, buy now on installments, and keep every order, warranty and repair in one dashboard.</p>" + Button(url, "Visit your dashboard")),
        $"Welcome to TechLandier, {name}! Visit your dashboard: {url}");

    public static SendEmail OrderPaid(string to, string? name, string orderNumber, string receipt, IReadOnlyList<(string Name, int Qty, decimal Total)> items, decimal total, string url) => new(to, $"Receipt {receipt} — order {orderNumber}",
        Layout($"Order {orderNumber} confirmed", $"<p>Hi {Esc(name)}, payment is in. Your order number is <b>{orderNumber}</b> and your receipt is <b>{receipt}</b>.</p>"
            + "<table style=\"width:100%;border-collapse:collapse\"><tr><th align=left>Item</th><th>Qty</th><th align=right>Total</th></tr>"
            + string.Concat(items.Select(i => $"<tr><td>{Esc(i.Name)}</td><td align=center>{i.Qty}</td><td align=right>{Money(i.Total)}</td></tr>"))
            + $"</table><p><b>Total {Money(total)}</b></p>" + Button(url, "Track your order")),
        $"Order {orderNumber} confirmed. Receipt {receipt}. Total {Money(total)}.\n{url}");

    public static SendEmail OrderShipped(string to, string? name, string orderNumber, string? rider, string eta, string url) => new(to, $"Order {orderNumber} shipped",
        Layout($"Order {orderNumber} is on the way", $"<p>Hi {Esc(name)}, your package has left the store.{(rider is null ? "" : $" {Esc(rider)} is on delivery duty.")} ETA: {Esc(eta)}.</p>" + Button(url, "Track your delivery")),
        $"Order {orderNumber} shipped. ETA: {eta}.\n{url}");

    public static SendEmail OrderCancelled(string to, string? name, string orderNumber, string reason, string url) => new(to, $"Order {orderNumber} cancelled",
        Layout($"Order {orderNumber} was cancelled", $"<p>Hi {Esc(name)}, this order was cancelled: {Esc(reason)}</p>" + Button(url, "View order history")),
        $"Order {orderNumber} cancelled: {reason}\n{url}");

    public static SendEmail OrderRefund(string to, string? name, string orderNumber, decimal amount, string url) => new(to, $"Refund for order {orderNumber}",
        Layout($"Refund issued — {Money(amount)}", $"<p>Hi {Esc(name)}, your payment for order <b>{orderNumber}</b> has been refunded in full ({Money(amount)}). Card statements can take 1–3 business days to show it.</p>" + Button(url, "View order")),
        $"Order {orderNumber} refunded ({Money(amount)}).\n{url}");

    public static SendEmail InstallmentPaid(string to, string? name, string planId, string orderNumber, int number, int count, decimal amount, decimal remaining, string url) => new(to, $"Installment {number}/{count} paid — {orderNumber}",
        Layout($"Installment {number} of {count} received", $"<p>Hi {Esc(name)}, your {Money(amount)} installment on <b>{orderNumber}</b> (plan {planId}) is in. {Money(remaining)} left on the plan.</p>" + Button(url, "Open your plan")),
        $"Installment {number}/{count} of {Money(amount)} paid on {orderNumber}; {Money(remaining)} remaining.\n{url}");

    public static SendEmail InstallmentReminder(string to, string? name, string orderNumber, int number, decimal amount, DateOnly dueDate, string url) => new(to, $"Installment {number} due {dueDate:MMM d} — {orderNumber}",
        Layout($"Installment {number} of {Money(amount)} due {dueDate:MMM d}", $"<p>Hi {Esc(name)}, your next installment for <b>{orderNumber}</b> is due <b>{dueDate:MMM d, yyyy}</b>. Pay early and stay out of overdue territory.</p>" + Button(url, "Pay installment")),
        $"Installment {number} of {Money(amount)} on {orderNumber} is due {dueDate:MM/dd/yyyy}.\n{url}");

    public static SendEmail InstallmentOverdue(string to, string? name, string orderNumber, int number, decimal amount, DateOnly dueDate, string url) => new(to, $"Overdue: installment {number} on {orderNumber}",
        Layout($"Installment {number} is overdue", $"<p>Hi {Esc(name)}, the {Money(amount)} installment for <b>{orderNumber}</b> was due <b>{dueDate:MMM d, yyyy}</b> and is now overdue. Overdue plans block new financing — pay as soon as you can.</p>" + Button(url, "Pay now")),
        $"Overdue installment {number} of {Money(amount)} on {orderNumber} (due {dueDate:MM/dd/yyyy}).\n{url}");

    public static SendEmail InstallmentStatement(string to, string? name, IReadOnlyList<(string OrderNumber, int Number, decimal Amount, DateOnly DueDate, string Status)> rows, string url) => new(to, "Your weekly TechLandier installment statement",
        Layout("Your weekly installment statement", "<p>Here is every open installment on your plans at a glance.</p>"
            + "<table style=\"width:100%;border-collapse:collapse\"><tr><th align=left>Order</th><th>#</th><th align=right>Amount</th><th>Due</th><th>Status</th></tr>"
            + string.Concat(rows.Select(r => $"<tr><td>{Esc(r.OrderNumber)}</td><td align=center>{r.Number}</td><td align=right>{Money(r.Amount)}</td><td align=center>{r.DueDate:MMM d}</td><td align=center>{r.Status.Replace('_', ' ')}</td></tr>"))
            + $"</table>{Button(url, "Manage plans")}"),
        "Weekly installment statement:\n" + string.Join("\n", rows.Select(r => $"- {r.OrderNumber} #{r.Number}: {Money(r.Amount)} due {r.DueDate:MM/dd/yyyy} ({r.Status})")) + $"\n{url}");

    public static SendEmail TicketReply(string to, string? name, string ticketNumber, string subject, string url) => new(to, $"Support replied — {ticketNumber}",
        Layout($"Reply on {ticketNumber}", $"<p>Hi {Esc(name)}, support answered your complaint <b>{Esc(subject)}</b> ({ticketNumber}). Read the reply and continue the thread if needed.</p>" + Button(url, "Open the thread")),
        $"Support replied on {ticketNumber} ({subject}).\n{url}");

    public static SendEmail CareDecision(string to, string? name, string kind, string item, string decision, string? note, string url) => new(to, $"Your {kind} update",
        Layout($"{kind} {Esc(decision)}", $"<p>Hi {Esc(name)}, an update on your <b>{Esc(item)}</b> {Esc(kind)}: <b>{Esc(decision)}</b>.</p>"
            + (note is { Length: > 0 } n ? $"<blockquote style=\"color:#444\">{Esc(n)}</blockquote>" : "")
            + Button(url, "Open your dashboard")),
        $"{kind} {decision} for {item}" + (note is { Length: > 0 } nn ? $"\n{nn}" : "") + $"\n{url}");
}
