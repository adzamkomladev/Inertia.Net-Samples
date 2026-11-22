using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using Inertia.Net.Testing;
using Inkwell.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Inkwell.Tests;

/// <summary>The app on a temp SQLite file, without background services. Share one per test class (IClassFixture).</summary>
public sealed class BlogFactory : WebApplicationFactory<Program>
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"inertiablog-it-{Guid.NewGuid():N}");
    private readonly Action<IWebHostBuilder>? _configure;

    public BlogFactory() : this(null) { }

    /// <summary>Every job the app staged or dispatched; Runwick itself is off in these tests.</summary>
    public RecordingJobOutbox Jobs { get; } = new();

    internal BlogFactory(Action<IWebHostBuilder>? configure)
    {
        _configure = configure;
        Directory.CreateDirectory(_dir);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Cron:Enabled", "false");
        builder.UseSetting("Runwick:Enabled", "false");
        builder.UseSetting("EMAIL_API_KEY", "");
        builder.UseSetting("DATABASE_URL", $"Data Source={Path.Combine(_dir, "app.db")}");
        builder.UseSetting("DATA_PROTECTION_KEYS_PATH", Path.Combine(_dir, "keys"));
        builder.UseSetting("S3_ENDPOINT", "http://127.0.0.1:1");
        // TestServer peers are not on a private network; trust all so each client's X-Forwarded-For gets its own bucket.
        builder.UseSetting("ForwardedHeaders:KnownNetworks:0", "0.0.0.0/0");
        builder.UseSetting("ForwardedHeaders:KnownNetworks:1", "::/0");
        builder.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(IHostedService)
                         && d.ImplementationType?.Name.Contains("Ssr") == true).ToList())
                services.Remove(d);
            // Pin the asset version: otherwise it's the Vite manifest hash when a frontend build exists, and the
            // test client (which sends no version) would get 409s.
            services.PostConfigure<InertiaOptions>(o => o.Version = "");
            services.AddSingleton<Inkwell.Jobs.IJobOutbox>(Jobs);
        });
        _configure?.Invoke(builder);
    }

    internal async Task DeliverJobsAsync(params string[] types) => await Jobs.DeliverAsync(Services, types);

    public T WithDb<T>(Func<AppDb, T> f)
    {
        using var scope = Services.CreateScope();
        return f(scope.ServiceProvider.GetRequiredService<AppDb>());
    }

    /// <summary>Creates a user straight in the database (optionally verified).</summary>
    public async Task<Guid> CreateUserAsync(string email, bool verified = true, string password = "Passw0rd!x")
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = email, Email = email, DisplayName = email.Split('@')[0], EmailConfirmed = verified, CreatedAt = DateTime.UtcNow };
        var r = await users.CreateAsync(user, password);
        Assert.True(r.Succeeded, string.Join(",", r.Errors.Select(e => e.Description)));
        return user.Id;
    }

    public static string NewEmail(string prefix = "u") => $"{prefix}{Guid.NewGuid():N}@example.com";

    /// <summary>The id of an existing user, for tests that need the live-channel key.</summary>
    public async Task<Guid> UserIdAsync(string email)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        return user.Id;
    }

    public Client NewClient() => new(this);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>A browser-like session: own cookies, no auto redirect, antiforgery token handling.</summary>
    public sealed class Client
    {
        private string? _token;
        public HttpClient Http { get; }

        private static int _ip;

        public Client(BlogFactory f)
        {
            Http = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
            // The app trusts X-Forwarded-For, so every session gets its own IP and therefore its own "auth" rate-limit bucket.
            var n = Interlocked.Increment(ref _ip);
            Http.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{n >> 16 & 255}.{n >> 8 & 255}.{n & 255}");
        }

        private void Capture(HttpResponseMessage r)
        {
            if (!r.Headers.TryGetValues("Set-Cookie", out var cookies)) return;
            foreach (var c in cookies.Where(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)))
                _token = WebUtility.UrlDecode(c["XSRF-TOKEN=".Length..].Split(';')[0]);
        }

        public async Task<HttpResponseMessage> GetAsync(string url)
        {
            var r = await Http.InertiaGetAsync(url);
            Capture(r);
            return r;
        }

        /// <summary>Unsafe request with the antiforgery header (fetches a fresh token first, so it is right after login/logout too).</summary>
        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, bool withToken = true, string? referer = null, bool precognition = false)
        {
            if (withToken) { using var _ = await GetAsync("/posts"); }
            var req = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body ?? new { }) };
            req.Headers.Add("X-Inertia", "true");
            req.Headers.Add("X-Requested-With", "XMLHttpRequest");
            req.Headers.Referrer = new Uri(Http.BaseAddress!, referer ?? url.Split('?')[0]); // Back() redirects to the referrer
            if (withToken) req.Headers.Add("X-XSRF-TOKEN", _token);
            if (precognition) req.Headers.Add("Precognition", "true");
            var r = await Http.SendAsync(req);
            Capture(r);
            return r;
        }

        public async Task<HttpResponseMessage> PostAsync(string url, object? body = null, string? referer = null) =>
            await SendAsync(HttpMethod.Post, url, body, referer: referer);

        public async Task<HttpResponseMessage> LoginAsync(string email, string password = "Passw0rd!x", string url = "/login") =>
            await PostAsync(url, new { email, password });

        public async Task LoginOkAsync(string email)
        {
            using var r = await LoginAsync(email);
            // Inertia visits render the feed inline after signing in; a plain form post (AllowAutoRedirect off) still
            // redirects there. Both land on /posts as Posts/Index.
            if (r.Headers.Location is { } location) Assert.Equal("/posts", location.OriginalString);
            else
            {
                var page = await r.GetInertiaPageAsync();
                Assert.Equal("/posts", page.Url);
                Assert.Equal("Posts/Index", page.Component);
            }
        }

        public async Task<Guid> CreatePostAsync(string title, string body)
        {
            using var r = await PostAsync("/posts", new { title, body });
            // Inertia visits render the essay page inline with the new post; a plain form post redirects to its url.
            if (r.Headers.Location is { } location) return Guid.Parse(location.OriginalString.Split('/').Last());
            var page = await r.GetInertiaPageAsync();
            Assert.Equal("Posts/Show", page.Component);
            return Guid.Parse(page.Url.Split('/').Last());
        }
    }
}
