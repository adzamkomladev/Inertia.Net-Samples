using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TechLandier.Infrastructure;

/// A rendered message; handlers build it and hand it to <see cref="Mailer"/>.
/// <paramref name="UnsubscribeUrl"/> is set on preference-backed mail and becomes the RFC 8058 one-click header.
public sealed record SendEmail(string To, string Subject, string Html, string Text, string? UnsubscribeUrl = null);

/// Maileroo HTTP API (https://maileroo.com/docs). Without an API key emails are only logged in Development; elsewhere sending throws.
public sealed class Mailer(HttpClient http, IConfiguration config, IHostEnvironment env, ILogger<Mailer> log)
{
    public async Task<string?> SendAsync(SendEmail mail, CancellationToken ct) =>
        await SendAsync(mail.To, mail.Subject, mail.Html, mail.Text, ct, mail.UnsubscribeUrl);

    public async Task<string?> SendAsync(string to, string subject, string html, string text, CancellationToken ct, string? unsubscribeUrl = null)
    {
        // Seed accounts must never reach an external mailbox, even with production credentials.
        // The .html twin is what the dev preview opens, so what a demo customer would see can be inspected.
        if (to.StartsWith("inertia.demo42.", StringComparison.OrdinalIgnoreCase) && to.EndsWith("@yopmail.com", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.Combine(config["App:DataDir"] is { Length: > 0 } data ? data : "storage", "captured-mail");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, Guid.NewGuid().ToString("N"));
            await File.WriteAllTextAsync(file + ".txt", $"To: {to}\nSubject: {subject}\n"
                + (unsubscribeUrl is null ? "" : $"List-Unsubscribe: <{unsubscribeUrl}>\n") + $"\n{text}", ct);
            await File.WriteAllTextAsync(file + ".html", html, ct);
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
                [new(to, null)], subject, html, text,
                // RFC 8058 one-click: mail clients POST to the URL, which the unsubscribe endpoint accepts without a session.
                unsubscribeUrl is null ? null : new Dictionary<string, string>
                {
                    ["List-Unsubscribe"] = $"<{unsubscribeUrl}>",
                    ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
                })),
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
        [property: JsonPropertyName("plain")] string Plain,
        [property: JsonPropertyName("headers"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Dictionary<string, string>? Headers);

    sealed record MailerooAddress(
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("display_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DisplayName);
}
