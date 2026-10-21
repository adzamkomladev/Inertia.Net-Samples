using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TechLandier.Data;
using TechLandier.Features;

namespace TechLandier.Tests;

/// Boots the real app against a throwaway PostgreSQL database, without SSR.
public class AppFactory : WebApplicationFactory<Program>
{
    readonly string _dataDir = Path.Combine(Path.GetTempPath(), "vis-tests-" + Guid.NewGuid().ToString("N"));
    readonly string _database = "techlandier_test_" + Guid.NewGuid().ToString("N");

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
        // A developer .env may point telemetry at SigNoz; test traffic must stay out of it.
        builder.UseSetting("Telemetry:Enabled", "false");
    }

    // The shared cookie jar lets every client reuse the antiforgery token once issued: 1.4's Lazy
    // refresh only writes Set-Cookie when the request carries no (valid) token, so re-reading it from
    // the jar is the reliable path for repeated POSTs from the same client.
    

    /// Every test client declares text/html so plain GETs count as page loads for the antiforgery
    /// pipeline (1.4 middleware only issues the XSRF-TOKEN cookie on page loads).
    public HttpClient Client()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions());
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html");
        // Page-load bootstrap: every fresh client must acquire the antiforgery token before its
        // first POST (1.4 middleware only issues the token on page loads).
        return Bootstrap(client);
    }

    string? _lastXsrf;

    HttpClient Bootstrap(HttpClient client)
    {
        using var response = client.GetAsync("/products").GetAwaiter().GetResult();
        foreach (var setCookie in response.Headers.GetValues("Set-Cookie"))
            if (setCookie.StartsWith("XSRF-TOKEN="))
                _lastXsrf = Uri.UnescapeDataString(setCookie["XSRF-TOKEN=".Length..setCookie.IndexOf(';')]);
        if (_lastXsrf is null)
            throw new InvalidOperationException("The antiforgery token was not issued on the storefront bootstrap.");
        return client;
    }


    string? _version;

    /// Asset version the server expects (hash of the built Vite manifest); a mismatch answers Inertia GETs with 409.
    public async Task<string> VersionAsync()
    {
        if (_version is not null) return _version;
        using var html = await Client().GetAsync("/products");
        if (!html.IsSuccessStatusCode) throw new InvalidOperationException(await html.Content.ReadAsStringAsync());
        var page = await Inertia.Net.Testing.InertiaPage.ParseAsync(html);
        return _version = page.Version ?? "";
    }

    public async Task<HttpResponseMessage> InertiaGetAsync(HttpClient client, string url) =>
        await Inertia.Net.Testing.InertiaClientExtensions.InertiaGetAsync(client, url, await VersionAsync());

    /// <summary>
    /// Partial reload the way the Inertia client fetches deferred props right after the page renders
    /// (same X-Inertia-Partial headers, only the requested prop is resolved server-side).
    /// </summary>
    public async Task<HttpResponseMessage> InertiaPartialAsync(
        HttpClient client,
        string url,
        string component,
        string prop
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Inertia", "true");
        request.Headers.Add("X-Inertia-Version", await VersionAsync());
        request.Headers.Add("X-Inertia-Partial-Component", component);
        request.Headers.Add("X-Inertia-Partial-Data", prop);
        return await client.SendAsync(request);
    }

    /// Registers through the real endpoint (antiforgery included) and optionally marks the email verified.
    public async Task<(HttpClient Client, Guid UserId)> SignUpAsync(string email, bool verified = true)
    {
        var client = Client();
        using var response = await SendAsync(client, "/register", new { name = "Test User", email, password = "correct-horse-battery", passwordConfirmation = "correct-horse-battery" });
        // Registration lands on Auth/VerifyEmail as a 200 Inertia render for fresh users (the earlier
        // versions answered a bare 303); assert on the outcome rather than the exact status shape.
        var bodyText = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK
                    && (bodyText.Contains("Auth/VerifyEmail") || bodyText.Contains(email)),
            $"register expected the verify-email landing; got {(int)response.StatusCode}: {Trunc(bodyText)}");

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

    /// A verified user with the admin role claim (ValidatePrincipal refreshes the cookie's claims from the database).
    public async Task<(HttpClient Client, Guid UserId)> AdminAsync(string email)
    {
        var (client, userId) = await SignUpAsync(email);
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, "admin"));
        return (client, userId);
    }

    public sealed record SeededProduct(Guid ProductId, Guid CategoryId, Guid ZoneId, Guid RiderId, decimal Price, int DiscountPercent, int Stock);

    /// Seeds an active product with its own category, a POD-enabled delivery zone and an active rider.
    public async Task<SeededProduct> SeedProductAsync(
        string name,
        decimal price = 199.99m,
        int discountPercent = 0,
        int stock = 10,
        int warrantyMonths = 0,
        bool insurable = false,
        decimal zoneFee = 4m)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var category = new Category { Slug = "cat-" + Guid.NewGuid().ToString("N"), Name = "Test Category", Icon = "Cpu" };
        var zone = new DeliveryZone { Name = "Test Zone", Fee = zoneFee, EtaText = "Same day if ordered by 2pm", AllowPayOnDelivery = true };
        var rider = new Rider { Name = "Test Rider", Phone = "+233 24 000 0000", Service = RiderServices.InHouse };
        var product = new Product
        {
            Slug = "product-" + Guid.NewGuid().ToString("N"),
            Name = name,
            Brand = "TestBrand",
            CategoryId = category.Id,
            Price = price,
            DiscountPercent = discountPercent,
            Stock = stock,
            WarrantyMonths = warrantyMonths,
            IsInsurable = insurable,
            Status = ProductStatuses.Active,
            Keywords = name,
        };
        db.AddRange(category, zone, rider, product);
        if (stock > 0)
            db.StockMovements.Add(new StockMovement { ProductId = product.Id, Delta = stock, Reason = StockReasons.Restock, Note = "Initial stock on create" });
        await db.SaveChangesAsync();
        return new SeededProduct(product.Id, category.Id, zone.Id, rider.Id, price, discountPercent, stock);
    }

    public async Task<T> QueryAsync<T>(Func<AppDb, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDb>());
    }

    /// An Inertia form submission: JSON body + X-Inertia + the antiforgery header.
    public async Task<HttpResponseMessage> SendAsync(HttpClient client, string url, object body, HttpMethod? method = null)
    {
        var token = await XsrfTokenAsync();
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Inertia", "true");
        request.Headers.Add("X-Inertia-Version", await VersionAsync());
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    /// Pre-fetch the antiforgery token so several clients can submit concurrently (racing checkouts).
    public Task<string> XsrfAsync(HttpClient client) => XsrfTokenAsync();

    /// An Inertia form submission with a pre-fetched antiforgery token.
    public Task<HttpResponseMessage> SendWithTokenAsync(HttpClient client, string token, string url, object body, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Inertia", "true");
        request.Headers.Add("X-XSRF-TOKEN", token);
        return client.SendAsync(request);
    }

    /// UseInertiaAntiforgeryCookie() writes XSRF-TOKEN on Inertia GETs; the client's cookie container keeps the matching .vis.af cookie.
    async Task<string> XsrfTokenAsync() =>
        _lastXsrf ?? throw new InvalidOperationException("No antiforgery token captured at bootstrap.");

    static string Trunc(string s) => s.Length > 300 ? s[..300] : s;

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
