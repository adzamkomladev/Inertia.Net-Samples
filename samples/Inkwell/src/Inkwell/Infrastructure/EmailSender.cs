using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Inkwell.Infrastructure;

public sealed class EmailOptions
{
    public string ApiKey { get; set; } = "";
    public string FromAddress { get; set; } = "no-reply@example.com";
    public string FromName { get; set; } = "Inkwell";
}

/// <summary>Sends mail through the Maileroo HTTP API. With no ApiKey it logs the message instead (dev).</summary>
public sealed class EmailSender(HttpClient http, IOptions<EmailOptions> options, ILogger<EmailSender> log, IConfiguration config, IHostEnvironment environment)
{
    private const string Endpoint = "https://smtp.maileroo.com/api/v2/emails";

    public async Task<string?> SendAsync(string toAddress, string? toName, string subject, string html, string plain, CancellationToken ct, string type = "transactional")
    {
        // Synthetic seed mail is captured locally and never reaches an external mailbox.
        if (toAddress.StartsWith("inertia.demo42.", StringComparison.OrdinalIgnoreCase)
            && toAddress.EndsWith("@yopmail.com", StringComparison.OrdinalIgnoreCase))
        {
            var root = config["App:DataDir"] is { Length: > 0 } data ? data : "storage";
            var directory = Path.Combine(root, "captured-mail");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".txt"),
                $"To: {toAddress}\nSubject: {subject}\n\n{plain}", ct);
            return null;
        }

        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.ApiKey))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("Email:ApiKey is not configured; cannot send email.");
            log.LogInformation("E-mail (no ApiKey configured, not sent) to {To}\nSubject: {Subject}\n{Body}", toAddress, subject, plain);
            return null;
        }

        // Anonymous members are already snake_case, which is what the API expects.
        var body = new
        {
            from = new { address = o.FromAddress, display_name = o.FromName },
            to = new[] { new { address = toAddress, display_name = toName ?? toAddress } },
            subject,
            html,
            plain,
            tags = new Dictionary<string, string> { ["type"] = type },
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(body) };
        req.Headers.Add("X-Api-Key", o.ApiKey);
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Maileroo returned HTTP {(int)res.StatusCode}.", null, res.StatusCode);
        return await TryReadProviderReferenceAsync(res, ct);
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
}
