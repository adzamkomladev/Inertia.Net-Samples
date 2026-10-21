using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SvelteMongoBlog.Infrastructure;

/// Maileroo HTTP API (https://maileroo.com/docs). Without an API key emails are only logged in Development; elsewhere sending throws.
public sealed class Mailer(HttpClient http, IConfiguration config, IHostEnvironment env, ILogger<Mailer> log)
{
    public async Task<string?> SendAsync(string to, string subject, string html, string text, CancellationToken ct)
    {
        // Synthetic seed mail is captured locally and never reaches an external mailbox.
        if (to.StartsWith("inertia.demo42.", StringComparison.OrdinalIgnoreCase)
            && to.EndsWith("@yopmail.com", StringComparison.OrdinalIgnoreCase))
        {
            var root = config["App:DataDir"] is { Length: > 0 } data ? data : "storage";
            var directory = Path.Combine(root, "captured-mail");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".txt"),
                $"To: {to}\nSubject: {subject}\n\n{text}", ct);
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
            throw new HttpRequestException($"Maileroo returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}", null, response.StatusCode);
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
                        && value.GetString() is { Length: > 0 and <= 256 } reference) return reference;
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
        <!doctype html><html><body style="font-family:system-ui,sans-serif;background:#f5f5f5;padding:24px">
        <div style="max-width:560px;margin:auto;background:#fff;border-radius:12px;padding:32px">
        <h1 style="font-size:20px;margin:0 0 16px">{{WebUtility.HtmlEncode(title)}}</h1>{{bodyHtml}}</div></body></html>
        """;

    static string Button(string url, string label) =>
        $"""<p><a href="{WebUtility.HtmlEncode(url)}" style="display:inline-block;background:#171717;color:#fff;padding:10px 18px;border-radius:8px;text-decoration:none">{WebUtility.HtmlEncode(label)}</a></p>""";

    public static SendEmail Verify(string to, string name, string url) => new(to, "Verify your email",
        Layout($"Welcome to CarParker, {name}!", $"<p>Confirm your email address to start booking parking spots.</p>{Button(url, "Verify email")}<p style=\"color:#666;font-size:13px\">This link expires in 24 hours.</p>"),
        $"Welcome to CarParker, {name}! Verify your email: {url} (expires in 24 hours)");

    /// Also enforces each user's preferences: caller passes the email only when the user opted in.
    public static SendEmail ReservationReminder(string to, string name, string lotName, string spotLabel, DateTime startUtc, string url) =>
        new(to, $"Parking reminder — {lotName}",
            Layout("Upcoming parking",
                $"<p>Hi {WebUtility.HtmlEncode(name)}, your spot <b>{WebUtility.HtmlEncode(spotLabel)}</b> at <b>{WebUtility.HtmlEncode(lotName)}</b> opens soon.</p>"
                + $"<p>Start (UTC): <b>{startUtc:u}</b></p>{Button(url, "Open your booking")}"),
            $"Hi {name}! Spot {spotLabel} at {lotName} opens at {startUtc:u}. Details: {url}");

    public static SendEmail RefundPaid(string to, string name, long amountCents, string reservationId, string url) =>
        new(to, "Your refund has been paid",
            Layout("Refund paid",
                $"<p>Hi {WebUtility.HtmlEncode(name)}, we've paid ${(amountCents / 100.0):F2} back to your original payment method for booking {reservationId[..8]}…</p>"
                + Button(url, "See your payments")),
            $"Hi {name}, your refund of {amountCents}c has been paid. Details: {url}");

    public static SendEmail ClaimDecision(string to, string name, string kind, bool approved, long? payoutCents, string note, string url)
    {
        var headline = approved ? "Your Spot Cover claim was approved" : "Your Spot Cover claim was rejected";
        var payoutLine = payoutCents is { } cents ? $"<p>Payout: <b>${cents / 100.0:F2}</b></p>" : "";
        return new SendEmail(to, headline,
            Layout(headline,
                $"<p>Hi {WebUtility.HtmlEncode(name)}, your <b>{WebUtility.HtmlEncode(kind)}</b> claim has been reviewed.</p>{payoutLine}"
                + $"<p style=\"color:#666\">Note: {WebUtility.HtmlEncode(note)}</p>{Button(url, "Open your insurance")}"),
            $"Hi {name}, your {kind} claim was {(approved ? "approved" : "rejected")}. Note: {note}\n{url}");
    }
}
