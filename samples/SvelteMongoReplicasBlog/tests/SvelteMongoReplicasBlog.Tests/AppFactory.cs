using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;

namespace SvelteMongoReplicasBlog.Tests;

/// Boots the real app against an isolated Mongo database, without SSR.
public class AppFactory : WebApplicationFactory<Program>
{
    readonly string _databaseName = "svelte_inertia_mongodb_test_" + Guid.NewGuid().ToString("N");
    readonly bool _meilisearchEnabled;

    public AppFactory() : this(false) { }
    internal AppFactory(bool meilisearchEnabled) => _meilisearchEnabled = meilisearchEnabled;

    /// Transactional coverage is the stricter default; SingleModeTests overrides this to exercise the default single mode.
    protected virtual string MongoMode => "replica";

    /// Dedicated test override. Do not read DATABASE_URL here: the app under test loads the repo .env into the process (DotNetEnv),
    /// and later fixtures would silently inherit it; the suite targets a throwaway replica set on its own.
    internal static string TestConnection => Environment.GetEnvironmentVariable("INERTIA_TEST_CONNECTION")
        ?? "mongodb://localhost:27017/?replicaSet=rs0&directConnection=true";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Cron:Enabled", "false");
        builder.UseSetting("Replicas:Enabled", "false");
        builder.UseSetting("MONGO_MODE", MongoMode);
        builder.UseSetting("EMAIL_API_KEY", "");
        builder.UseSetting("Maileroo:ApiKey", "");
        builder.UseSetting("DATABASE_NAME", _databaseName);
        builder.UseSetting("DATABASE_URL", TestConnection);
        builder.UseSetting("App:BaseUrl", "http://localhost");
        builder.UseSetting("Inertia:Ssr", "false");
        var meilisearchUrl = _meilisearchEnabled ? "http://127.0.0.1:1" : "";
        builder.UseSetting("Meilisearch:Url", meilisearchUrl);
        builder.UseSetting("MEILISEARCH_URL", meilisearchUrl);
        builder.UseSetting("Meilisearch:ApiKey", _meilisearchEnabled ? "test-key" : "");
        builder.UseSetting("MEILISEARCH_API_KEY", _meilisearchEnabled ? "test-key" : "");
        builder.UseSetting("CACHE_PROVIDER", "memory");
        builder.UseSetting("REDIS_URL", "");
        builder.UseSetting("SEARCH_DRIVER", _meilisearchEnabled ? "meilisearch" : "native");
        builder.UseSetting("APP_NAME", "Plain Name"); // plain env-style name, see Plain_env_names_override_config_keys
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    string? _version;

    /// Asset version the server expects (hash of the built Vite manifest); a mismatch answers Inertia GETs with 409.
    public async Task<string> VersionAsync()
    {
        if (_version is not null) return _version;
        using var html = await Client().GetAsync("/posts");
        if (!html.IsSuccessStatusCode) throw new InvalidOperationException($"GET /posts failed: {await html.Content.ReadAsStringAsync()}");
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
        var user = await db.Users.Find(u => u.Email == email).SingleAsync();
        if (verified)
        {
            user.EmailVerifiedAt = DateTime.UtcNow;
            await db.Users.ReplaceOneAsync(u => u.Id == user.Id, user);
        }
        return (client, user.Id);
    }

    /// UUIDv7 ids with strictly increasing timestamps: Guid.CreateVersion7() is not monotonic within one millisecond.
    public static Guid[] OrderedIds(int count)
    {
        var start = DateTimeOffset.UtcNow;
        return Enumerable.Range(0, count).Select(i => Guid.CreateVersion7(start.AddMilliseconds(i))).ToArray();
    }

    public async Task<Guid> PostIdAsync(string title)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDb>().Posts.Find(p => p.Title == title).Project(p => p.Id).SingleAsync();
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

    /// UseInertiaAntiforgeryCookie() writes XSRF-TOKEN on Inertia GETs; the client's cookie container keeps the matching .vis.af cookie.
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
        var client = Services.GetRequiredService<IMongoClient>();
        await client.DropDatabaseAsync(_databaseName);
        await base.DisposeAsync();
    }
}
