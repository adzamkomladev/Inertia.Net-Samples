using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SvelteMongoReplicasBlog.Infrastructure;

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
        Layout($"Welcome, {name}!", $"<p>Confirm your email address to start posting.</p>{Button(url, "Verify email")}<p style=\"color:#666;font-size:13px\">This link expires in 24 hours.</p>"),
        $"Welcome, {name}! Verify your email: {url} (expires in 24 hours)");

    public static SendEmail NewComment(string to, string commenter, string postTitle, string url, string excerpt) => new(to, $"New comment on \"{postTitle}\"",
        Layout("New comment", $"<p><b>{WebUtility.HtmlEncode(commenter)}</b> commented on <b>{WebUtility.HtmlEncode(postTitle)}</b>:</p><blockquote style=\"color:#444\">{WebUtility.HtmlEncode(excerpt)}</blockquote>{Button(url, "View comment")}"),
        $"{commenter} commented on \"{postTitle}\": {excerpt}\n{url}");

    public static SendEmail NewLike(string to, string liker, string postTitle, string url) => new(to, $"{liker} liked \"{postTitle}\"",
        Layout("New like", $"<p><b>{WebUtility.HtmlEncode(liker)}</b> liked <b>{WebUtility.HtmlEncode(postTitle)}</b>.</p>{Button(url, "View post")}"),
        $"{liker} liked \"{postTitle}\".\n{url}");

    public static SendEmail Digest(string to, string name, IReadOnlyList<(string Title, int Views, int Likes, int Comments)> rows, string url) => new(to, "Your weekly post stats",
        Layout($"Your week, {name}", "<table style=\"width:100%;border-collapse:collapse\"><tr><th align=left>Post</th><th>Views</th><th>Likes</th><th>Comments</th></tr>"
            + string.Concat(rows.Select(r => $"<tr><td>{WebUtility.HtmlEncode(r.Title)}</td><td align=center>{r.Views}</td><td align=center>{r.Likes}</td><td align=center>{r.Comments}</td></tr>"))
            + $"</table>{Button(url, "Open analytics")}"),
        $"Your week, {name}:\n" + string.Join("\n", rows.Select(r => $"- {r.Title}: {r.Views} views, {r.Likes} likes, {r.Comments} comments")) + $"\n{url}");

    public static SendEmail Recommendations(string to, string name, IReadOnlyList<(string Title, string Excerpt, string Url)> posts, string unsubscribeUrl)
    {
        var rows = string.Concat(posts.Select(post => $"<li style=\"margin:0 0 18px\"><a href=\"{WebUtility.HtmlEncode(post.Url)}\"><b>{WebUtility.HtmlEncode(post.Title)}</b></a><p>{WebUtility.HtmlEncode(post.Excerpt)}</p></li>"));
        var html = Layout($"Don't miss these posts, {name}", $"<p>Here are a few new posts from yesterday picked for you.</p><ul>{rows}</ul><p style=\"font-size:12px;color:#666\"><a href=\"{WebUtility.HtmlEncode(unsubscribeUrl)}\">Turn off daily recommendations</a></p>");
        var text = $"Don't miss these posts, {name}:\n" + string.Join("\n", posts.Select(post => $"- {post.Title}: {post.Url}")) + $"\nTurn off daily recommendations: {unsubscribeUrl}";
        return new SendEmail(to, "Don't miss these posts", html, text);
    }
}
