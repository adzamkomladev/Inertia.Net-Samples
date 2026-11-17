using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarParker.Infrastructure;

/// Maileroo HTTP API (https://maileroo.com/docs). Without an API key emails are only logged in Development; elsewhere sending throws.
public sealed class Mailer(HttpClient http, IConfiguration config, IHostEnvironment env, ILogger<Mailer> log)
{
    public async Task<string?> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var (to, subject, html, text) = message;
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

/// <summary>A rendered message. Built inside job handlers from ids, never stored in a job payload.</summary>
public sealed record EmailMessage(string To, string Subject, string Html, string Text);
