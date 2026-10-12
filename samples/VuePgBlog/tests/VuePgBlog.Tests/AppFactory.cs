using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VuePgBlog.Data;

namespace VuePgBlog.Tests;

/// Boots the real app against a throwaway PostgreSQL database, without SSR.
public class AppFactory : WebApplicationFactory<Program>
{
    readonly string _dataDir = Path.Combine(Path.GetTempPath(), "vis-tests-" + Guid.NewGuid().ToString("N"));
    readonly string _database = "vue_inertia_test_" + Guid.NewGuid().ToString("N");

    public AppFactory()
    {
        var admin = new NpgsqlConnectionStringBuilder(DatabaseConnection.Parse(
            Environment.GetEnvironmentVariable("INERTIA_TEST_CONNECTION")
            ?? "Host=localhost;Port=55432;Username=inertiablog;Password=local-dev-password")) { Database = "postgres" };
        using var connection = new NpgsqlConnection(admin.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
        command.ExecuteNonQuery();
    }

    string Connection => new NpgsqlConnectionStringBuilder(DatabaseConnection.Parse(
        Environment.GetEnvironmentVariable("INERTIA_TEST_CONNECTION")
        ?? "Host=localhost;Port=55432;Username=inertiablog;Password=local-dev-password")) { Database = _database }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Cron:Enabled", "false");
        builder.UseSetting("DATABASE_URL", Connection);
        builder.UseSetting("App:DataDir", _dataDir);
        builder.UseSetting("App:BaseUrl", "http://localhost");
        builder.UseSetting("Inertia:Ssr", "false");
        builder.UseSetting("SEARCH_DRIVER", "native");
        builder.UseSetting("REDIS_URL", "");
        builder.UseSetting("MEILISEARCH_URL", "");
        builder.UseSetting("EMAIL_API_KEY", "");
        builder.UseSetting("APP_NAME", "Plain Name"); // plain env-style name, see Plain_env_names_override_config_keys
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

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
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        var admin = new NpgsqlConnectionStringBuilder(Connection) { Database = "postgres" };
        using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            connection.Open();
            using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
            command.ExecuteNonQuery();
        }
        try { Directory.Delete(_dataDir, recursive: true); } catch (IOException) { }
    }
}
