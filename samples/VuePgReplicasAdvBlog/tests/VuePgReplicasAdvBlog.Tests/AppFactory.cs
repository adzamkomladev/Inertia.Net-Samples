extern alias main;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Infrastructure;
using VuePgReplicasAdvBlog.Infrastructure.Setup;
using VuePgReplicasAdvBlog.Worker;

namespace VuePgReplicasAdvBlog.Tests;

/// Boots the real app against a throwaway PostgreSQL database, without SSR.
public class AppFactory : WebApplicationFactory<main::Program>
{
    readonly string _dataDir = Path.Combine(Path.GetTempPath(), "vis-tests-" + Guid.NewGuid().ToString("N"));
    readonly string _database = "vue_inertia_replicas_test_" + Guid.NewGuid().ToString("N");
    readonly string? _sharedConnection;
    readonly string? _redis;
    readonly string? _replicaNamespace;

    public AppFactory() : this(null, null, null) { }

    internal AppFactory(string? sharedConnection, string? redis, string? replicaNamespace)
    {
        _sharedConnection = sharedConnection;
        _redis = redis ?? Environment.GetEnvironmentVariable("INERTIA_TEST_REDIS") ?? "redis://127.0.0.1:6387";
        _replicaNamespace = replicaNamespace ?? "advanced-tests:" + _database;
        if (sharedConnection is not null) return;
        var admin = new NpgsqlConnectionStringBuilder(DatabaseConnection.Parse(
            Environment.GetEnvironmentVariable("INERTIA_TEST_CONNECTION")
            ?? "Host=localhost;Port=5441;Username=vue_pg_replicas_adv_blog;Password=local-dev-password"))
        { Database = "postgres" };
        using var connection = new NpgsqlConnection(admin.ConnectionString);
        connection.Open();
        // WAL_LOG avoids forcing a cluster-wide checkpoint for every parallel test fixture.
        using var command = new NpgsqlCommand($"CREATE DATABASE \"{_database}\" STRATEGY WAL_LOG", connection);
        command.ExecuteNonQuery();
    }

    internal string Connection => _sharedConnection ?? new NpgsqlConnectionStringBuilder(DatabaseConnection.Parse(
        Environment.GetEnvironmentVariable("INERTIA_TEST_CONNECTION")
        ?? "Host=localhost;Port=5441;Username=vue_pg_replicas_adv_blog;Password=local-dev-password"))
    { Database = _database }.ConnectionString;

    IHost? _worker;
    WebApplicationFactory<Realtime.RealtimeHost>? _realtime;

    // Independent processes in production; independent hosts and service providers in tests.
    public IServiceProvider WorkerServices
    {
        get
        {
            _ = base.Services; // MainApp migrates the disposable test database first.
            if (_worker is null)
            {
                var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                {
                    EnvironmentName = "Development",
                    ContentRootPath = Path.GetFullPath("../Worker", base.Services.GetRequiredService<IHostEnvironment>().ContentRootPath)
                });
                builder.Configuration.AddInMemoryCollection(TestSettings);
                builder.Configuration.ApplyPlainNames();
                builder.AddServiceDefaults();
                builder.AddCommonData().AddCommonInfrastructure().AddWorkerServices();
                _worker = builder.Build();
                _worker.StartAsync().GetAwaiter().GetResult();
            }
            return _worker.Services;
        }
    }

    public async Task StopWorkerAsync()
    {
        if (_worker is not null) { await _worker.StopAsync(); _worker.Dispose(); }
        _worker = null;
    }

    public HttpClient RealtimeClient()
    {
        _ = base.Services;
        _realtime ??= new WebApplicationFactory<Realtime.RealtimeHost>().WithWebHostBuilder(ConfigureWebHost);
        return _realtime.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public Microsoft.AspNetCore.TestHost.TestServer RealtimeServer
    {
        get { using var client = RealtimeClient(); return _realtime!.Server; }
    }

    public async Task<HttpClient> RealtimeClientAsync(HttpClient signedIn, Guid userId)
    {
        var email = await QueryAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync());
        using var login = await SendAsync(signedIn, "/login", new { email, password = "correct-horse-battery" });
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".vpgmra.auth=")).Split(';')[0];
        var realtime = RealtimeClient();
        realtime.DefaultRequestHeaders.Add("Cookie", cookie);
        return realtime;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        foreach (var setting in TestSettings) builder.UseSetting(setting.Key, setting.Value);
    }

    Dictionary<string, string?> TestSettings => new()
    {
        ["Cron:Enabled"] = "false",
        ["DATABASE_URL"] = Connection,
        ["App:DataDir"] = _dataDir,
        ["App:BaseUrl"] = "http://localhost",
        ["Inertia:Ssr"] = "false",
        ["SEARCH_DRIVER"] = "native",
        ["REDIS_URL"] = _redis,
        ["Replicas:Enabled"] = "true",
        ["Replicas:Namespace"] = _replicaNamespace,
        ["MEILISEARCH_URL"] = "",
        ["EMAIL_API_KEY"] = "",
        ["Maileroo:ApiKey"] = "",
        ["APP_NAME"] = "Plain Name"
    };

    int _clientNumber;

    public HttpClient Client()
    {
        _ = WorkerServices;
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        // TestServer has no peer IP; give each simulated browser a stable address for real rate-limit policies.
        var number = Interlocked.Increment(ref _clientNumber);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.240.{number / 250}.{number % 250 + 1}");
        return client;
    }

    string? _version;

    /// Asset version the server expects (hash of the built Vite manifest); a mismatch answers Inertia GETs with 409.
    public async Task<string> VersionAsync()
    {
        if (_version is not null) return _version;
        using var html = await Client().GetAsync("/posts");
        if (!html.IsSuccessStatusCode) throw new InvalidOperationException(await html.Content.ReadAsStringAsync());
        var page = await Inertia.Net.Testing.InertiaPage.ParseAsync(html);
        return _version = page.Version ?? "";
    }

    public async Task<HttpResponseMessage> InertiaGetAsync(HttpClient client, string url) =>
        await Inertia.Net.Testing.InertiaClientExtensions.InertiaGetAsync(client, url, await VersionAsync());

    /// Registers through the real endpoint (antiforgery included) and optionally marks the email verified.
    public async Task<(HttpClient Client, Guid UserId)> SignUpAsync(string email, bool verified = true)
    {
        var client = Client();
        using var response = await SendAsync(client, "/register", new { name = "Test User", email, password = "correct-horse-battery", passwordConfirmation = "correct-horse-battery" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        if (verified)
        {
            user.EmailVerifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        return (client, user.Id);
    }

    public async Task<Guid> PostIdAsync(string title)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDb>().Posts.Where(p => p.Title == title).Select(p => p.Id).SingleAsync();
    }

    public async Task<T> QueryAsync<T>(Func<AppDb, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDb>());
    }

    /// An Inertia form submission: JSON body + X-Inertia + the antiforgery header.
    public async Task<HttpResponseMessage> SendAsync(HttpClient client, string url, object body, HttpMethod? method = null)
    {
        var token = await XsrfTokenAsync(client, await VersionAsync());
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Inertia", "true");
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    /// UseInertiaAntiforgeryCookie() writes XSRF-TOKEN on Inertia GETs; the client keeps the matching antiforgery cookie.
    static async Task<string> XsrfTokenAsync(HttpClient client, string version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/posts");
        request.Headers.Add("X-Inertia", "true");
        request.Headers.Add("X-Inertia-Version", version);
        using var response = await client.SendAsync(request);
        var cookie = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("XSRF-TOKEN="));
        return Uri.UnescapeDataString(cookie["XSRF-TOKEN=".Length..cookie.IndexOf(';')]);
    }

    bool _disposed;

    // xUnit calls DisposeAsync and then Dispose (which calls DisposeAsync again); a second host stop throws
    // ObjectDisposedException from Inertia.Net's SsrNodeProcess.StopAsync, so dispose exactly once.
    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_realtime is not null) await _realtime.DisposeAsync();
        await StopWorkerAsync();
        await base.DisposeAsync();
        if (_sharedConnection is null)
        {
            NpgsqlConnection.ClearAllPools();
            var admin = new NpgsqlConnectionStringBuilder(Connection) { Database = "postgres" };
            using var connection = new NpgsqlConnection(admin.ConnectionString);
            connection.Open();
            using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
            command.ExecuteNonQuery();
        }
        try { Directory.Delete(_dataDir, recursive: true); } catch (IOException) { }
    }
}
