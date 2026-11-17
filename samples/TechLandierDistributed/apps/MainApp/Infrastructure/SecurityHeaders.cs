using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TechLandier.Infrastructure;

/// Baseline security headers plus a Content-Security-Policy with a per-request script nonce.
/// Inertia.Net stamps that nonce (InertiaOptions.CspNonce) on the page-data script and the Vite tags; the one static inline
/// theme script in app.html is allowed by its SHA-256 instead, because server-rendered pages come straight from the SSR
/// view and could not carry a per-request attribute on it.
/// Styles keep 'unsafe-inline': the placeholder shell and critical CSS are inline, Vue sets style attributes, and a style
/// injection cannot run script.
public sealed partial class SecurityHeadersMiddleware
{
    public const string NonceItem = "csp-nonce";

    readonly RequestDelegate next;
    readonly IWebHostEnvironment env;
    readonly string[] storageOrigins;
    readonly string htmlPath;
    readonly bool development;

    // app.html is re-hashed only when the file changes (so editing the theme script in development still works).
    readonly Lock gate = new();
    string[] inlineHashes = [];
    DateTime hashStamp;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env, Storage storage)
    {
        this.next = next;
        this.env = env;
        storageOrigins = [.. storage.BrowserOrigins];
        htmlPath = Path.Combine(env.ContentRootPath, "app.html");
        development = env.IsDevelopment();
    }

    /// The request's nonce, or null before the middleware ran.
    public static string? Nonce(HttpContext ctx) => ctx.Items[NonceItem] as string;

    public async Task InvokeAsync(HttpContext ctx)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        ctx.Items[NonceItem] = nonce;
        ctx.Response.OnStarting(async () =>
        {
            var h = ctx.Response.Headers;
            h.XContentTypeOptions = "nosniff";
            h.XFrameOptions = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            // Only HTML documents carry a policy; JSON, assets and the SignalR transports do not execute anything.
            if (ctx.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
                h.ContentSecurityPolicy = Policy(ctx, nonce);
            await Task.CompletedTask; // OnStarting callbacks must return a Task; nothing here is asynchronous
        });
        await next(ctx);
    }

    string Policy(HttpContext ctx, string nonce)
    {
        var hashes = InlineScriptHashes();
        var host = ctx.Request.Host.Value;
        var ws = ctx.Request.IsHttps ? "wss" : "ws";
        var scripts = new List<string> { "'self'", $"'nonce-{nonce}'" };
        scripts.AddRange(hashes.Select(h => $"'sha256-{h}'"));
        var connect = new List<string> { "'self'", $"{ws}://{host}" };
        connect.AddRange(storageOrigins);
        var images = new List<string> { "'self'", "data:", "blob:" };
        images.AddRange(storageOrigins);
        var styles = new List<string> { "'self'", "'unsafe-inline'", "https://fonts.googleapis.com" };

        if (development)
        {
            // Vite serves modules and its HMR socket from its own origin (the same one wwwroot/hot records).
            foreach (var origin in ViteOrigins())
            {
                scripts.Add(origin);
                styles.Add(origin);
                images.Add(origin);
                connect.Add(origin);
                connect.Add(origin.Replace("http", "ws", StringComparison.Ordinal));
            }
        }

        var policy = new StringBuilder()
            .Append("default-src 'self'; ")
            .Append("script-src ").AppendJoin(' ', scripts).Append("; ")
            .Append("style-src ").AppendJoin(' ', styles).Append("; ")
            .Append("font-src 'self' https://fonts.gstatic.com data:; ")
            .Append("img-src ").AppendJoin(' ', images).Append("; ")
            .Append("connect-src ").AppendJoin(' ', connect).Append("; ")
            .Append("manifest-src 'self'; ")
            .Append("frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self'");
        // Only when the request really arrived over HTTPS (directly or via X-Forwarded-Proto): on a plain-HTTP origin,
        // such as the local compose stack, the directive would upgrade redirects to an https:// port nothing serves.
        if (!development && ctx.Request.IsHttps) policy.Append("; upgrade-insecure-requests");
        return policy.ToString();
    }

    IEnumerable<string> ViteOrigins()
    {
        var hot = Path.Combine(env.ContentRootPath, "wwwroot", "hot");
        var origins = new List<string> { "http://localhost:5173", "http://127.0.0.1:5173" };
        try
        {
            if (File.Exists(hot) && Uri.TryCreate(File.ReadAllText(hot).Trim(), UriKind.Absolute, out var uri))
                origins.Add(uri.GetLeftPart(UriPartial.Authority));
        }
        catch (IOException)
        {
            // The dev server is rewriting its hot file; the defaults still cover the usual port.
        }
        return origins.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    string[] InlineScriptHashes()
    {
        if (!File.Exists(htmlPath)) return [];
        var stamp = File.GetLastWriteTimeUtc(htmlPath);
        lock (gate)
        {
            if (stamp != hashStamp)
            {
                inlineHashes = [.. InlineScript().Matches(File.ReadAllText(htmlPath))
                    .Select(m => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(m.Groups[1].Value))))];
                hashStamp = stamp;
            }
            return inlineHashes;
        }
    }

    // Bare <script>...</script> blocks (no attributes, no src): the inline scripts a CSP has to allow by hash.
    [GeneratedRegex(@"<script>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex InlineScript();
}
