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

namespace Inkwell.Tests;

/// <summary>The app on a temp SQLite file, without background services. Share one per test class (IClassFixture).</summary>
public sealed class BlogFactory : WebApplicationFactory<Program>
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"inertiablog-it-{Guid.NewGuid():N}");
    private readonly Action<IWebHostBuilder>? _configure;

    public BlogFactory() : this(null) { }

    internal BlogFactory(Action<IWebHostBuilder>? configure)
    {
        _configure = configure;
        Directory.CreateDirectory(_dir);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Cron:Enabled", "false");
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
                         && (d.ImplementationType?.Name is "JobWorker" || d.ImplementationType?.Name.Contains("Ssr") == true)).ToList())
                services.Remove(d);
            // Pin the asset version: otherwise it's the Vite manifest hash when a frontend build exists, and the
            // test client (which sends no version) would get 409s.
            services.PostConfigure<InertiaOptions>(o => o.Version = "");
        });
        _configure?.Invoke(builder);
    }

    internal async Task DeliverJobsAsync(params string[] types)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var registry = scope.ServiceProvider.GetRequiredService<Inkwell.Jobs.JobRegistry>();
        while (true)
        {
            var jobs = await db.Jobs.Where(j => j.Status == JobStatus.Pending && types.Contains(j.Type)).ToListAsync();
            if (jobs.Count == 0) return;
            foreach (var job in jobs)
            {
                if (!registry.TryGet(job.Type, out var run)) throw new InvalidOperationException($"Missing handler: {job.Type}");
                await run(scope.ServiceProvider, job.Payload, CancellationToken.None);
                job.Status = JobStatus.Done;
            }
            await db.SaveChangesAsync();
        }
    }

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
            Assert.Equal("/posts", r.Headers.Location?.OriginalString);
        }

        public async Task<Guid> CreatePostAsync(string title, string body)
        {
            using var r = await PostAsync("/posts", new { title, body });
            Assert.True(r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther, r.StatusCode.ToString());
            return Guid.Parse(r.Headers.Location!.OriginalString.Split('/').Last());
        }
    }
}
