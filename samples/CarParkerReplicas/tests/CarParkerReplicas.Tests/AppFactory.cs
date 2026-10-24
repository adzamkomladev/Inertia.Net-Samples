using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Bookings;
using CarParker.Features.Lots;

namespace CarParker.Tests;

/// Boots the real app against an isolated Mongo database, without SSR.
public class AppFactory : WebApplicationFactory<Program>
{
    readonly string _databaseName = "carparker_test_" + Guid.NewGuid().ToString("N");
    readonly System.Collections.Concurrent.ConcurrentDictionary<UserRole, Task<(HttpClient, Guid)>> _sharedUsers = new();

    /// Dedicated test override. Do not read DATABASE_URL here: the app under test loads the repo .env into the process (DotNetEnv),
    /// and later fixtures would silently inherit it; the suite targets a disposable server explicitly.
    internal static string TestConnection => Environment.GetEnvironmentVariable("INERTIA_TEST_CONNECTION")
        ?? "mongodb://localhost:27017/?replicaSet=rs0&directConnection=true";

    internal static string TestMode => Environment.GetEnvironmentVariable("INERTIA_TEST_MONGO_MODE") ?? "replica";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Cron:Enabled", "false");
        builder.UseSetting("EMAIL_API_KEY", "");
        builder.UseSetting("Maileroo:ApiKey", "");
        builder.UseSetting("DATABASE_NAME", _databaseName);
        builder.UseSetting("DATABASE_URL", TestConnection);
        builder.UseSetting("MONGO_MODE", TestMode);
        builder.UseSetting("App:BaseUrl", "http://localhost");
        builder.UseSetting("Inertia:Ssr", "false");
        builder.UseSetting("APP_NAME", "Plain Name"); // plain env-style name, see Plain_env_names_override_config_keys
        // Default tests must not need Docker/Redis; the Redis cache tests opt in with REDIS_URL.
        builder.UseSetting("Replicas:Enabled", "false");
        builder.UseSetting("REDIS_URL", "");
        builder.UseSetting("Redis:Url", "");
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    string? _version;

    /// Asset version the server expects (hash of the built Vite manifest); a mismatch answers Inertia GETs with 409.
    public async Task<string> VersionAsync()
    {
        if (_version is not null) return _version;
        using var html = await Client().GetAsync("/lots");
        if (!html.IsSuccessStatusCode) throw new InvalidOperationException($"GET /lots failed: {await html.Content.ReadAsStringAsync()}");
        var page = await Inertia.Net.Testing.InertiaPage.ParseAsync(html);
        return _version = page.Version ?? "";
    }

    public async Task<HttpResponseMessage> InertiaGetAsync(HttpClient client, string url) =>
        await Inertia.Net.Testing.InertiaClientExtensions.InertiaGetAsync(client, url, await VersionAsync());

    /// Registers through the real endpoint (antiforgery included) and optionally marks the email verified.
    public async Task<(HttpClient Client, Guid UserId)> SignUpAndVerifyAsync(string email, bool verified = true)
    {
        var client = Client();
        using var response = await SendAsync(client, "/register", new { name = "Test User", email, password = Password, passwordConfirmation = Password });
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

    internal const string Password = "correct-horse-battery";
    static readonly PasswordHasher<User> Hasher = new();

    /// Seeds a verified user with any role directly in Mongo (staff roles have no sign-up) and signs in through
    /// the real /login endpoint so the auth cookie carries the role claims.
    public async Task<(HttpClient Client, Guid UserId)> SeedUserAsync(UserRole role = UserRole.Customer, string? email = null)
    {
        email ??= $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        var user = new User
        {
            Email = email.ToLowerInvariant(),
            Name = $"Test {role}",
            Role = role,
            EmailVerifiedAt = DateTime.UtcNow,
            PasswordHash = Hasher.HashPassword(new User { Email = "", Name = "" }, Password),
        };
        await QueryAsync(async db => { await db.Users.InsertOneAsync(user); return true; });
        var client = Client();
        using var login = await SendAsync(client, "/login", new { email = user.Email, password = Password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return (client, user.Id);
    }

    /// One seeded user per role per fixture: the /login rate limit allows 10 attempts per minute per IP.
    public async Task<(HttpClient Client, Guid UserId)> SharedUserAsync(UserRole role = UserRole.Customer) =>
        await _sharedUsers.GetOrAdd(role, async r => await SeedUserAsync(r));

    /// A minimal lot with a fresh grid of free spots, for booking tests.
    public async Task<(Lot Lot, List<Spot> Spots)> SeedLotAsync(string name, long ratePerHourCents = 400,
        long dailyMaxCents = 2400, long coverageCents = 50_000, int spotCount = 2, bool active = true, SpotType[]? types = null,
        string area = "Testville")
    {
        var lotId = Guid.CreateVersion7();
        return await QueryAsync(async db =>
        {
            var lot = new Lot
            {
                Id = lotId, Name = name, Address = "1 Test Street", Area = area,
                RatePerHourCents = ratePerHourCents, DailyMaxCents = dailyMaxCents,
                CoverageCents = coverageCents, IsActive = active,
            };
            await db.Lots.InsertOneAsync(lot);
            var spots = Enumerable.Range(1, spotCount).Select(i => new Spot
            {
                LotId = lotId, Label = $"A-{i:00}", Level = 1,
                Type = types is { Length: > 0 } && types.Length >= i ? types[i - 1] : SpotType.Standard,
            }).ToList();
            await db.Spots.InsertManyAsync(spots);
            return (lot, spots);
        });
    }

    /// UUIDv7 ids with strictly increasing timestamps: Guid.CreateVersion7() is not monotonic within one millisecond.
    public static Guid[] OrderedIds(int count)
    {
        var start = DateTimeOffset.UtcNow;
        return Enumerable.Range(0, count).Select(i => Guid.CreateVersion7(start.AddMilliseconds(i))).ToArray();
    }

    public async Task<T> QueryAsync<T>(Func<AppDb, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDb>());
    }

    // ---------- customer booking helpers (the write paths the domain tests lean on) ----------

    public async Task<HttpResponseMessage> BookAsync(HttpClient client, Lot lot, Spot spot, DateTime start, DateTime end,
        string method = "card", bool insurance = false, string? attemptId = null) =>
        await SendAsync(client, "/account/bookings", new
        {
            lotId = lot.Id, spotId = spot.Id, windowType = "single",
            startAtUtc = start, endAtUtc = end, method, insurance, attemptId, plateNo = "CP-01",
        });

    public async Task<HttpResponseMessage> BookRecurringAsync(HttpClient client, Lot lot, Spot spot,
        string daysOfWeek, string timeFrom, string timeTo, int weeks, string method = "card") =>
        await SendAsync(client, "/account/bookings", new
        {
            lotId = lot.Id, spotId = spot.Id, windowType = "recurring",
            daysOfWeek, timeFrom, timeTo, weeks, method, plateNo = "CP-01",
        });

    public async Task<HttpResponseMessage> PayBookingAsync(HttpClient client, Guid reservationId, string method = "card") =>
        await SendAsync(client, $"/account/bookings/{reservationId}/pay", new { method });

    public async Task<HttpResponseMessage> CancelBookingAsync(HttpClient client, Guid reservationId) =>
        await SendAsync(client, $"/account/bookings/{reservationId}/cancel", new { });

    public async Task<Reservation?> ReservationAsync(Guid id)
    {
        Reservation? reservation = await QueryAsync(async db => await db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync());
        return reservation;
    }

    /// The reservation id a booking POST redirected to (/account/bookings/{id}).
    public static Guid BookingIdOf(HttpResponseMessage created) =>
        Guid.Parse(created.Headers.Location!.OriginalString.Split('/').Last());

    /// An Inertia form submission: JSON body + X-Inertia + the antiforgery header.
    public async Task<HttpResponseMessage> SendAsync(HttpClient client, string url, object body, HttpMethod? method = null, bool precognition = false)
    {
        var token = await XsrfTokenAsync(client, await VersionAsync());
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (precognition) request.Headers.Add("Precognition", "true");
        request.Headers.Add("X-Inertia", "true");
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HttpClient, System.Runtime.CompilerServices.StrongBox<string>> XsrfTokens = new();

    /// UseInertiaAntiforgery() (XsrfCookieRefresh.Lazy) writes XSRF-TOKEN on page loads only while the client carries no
    /// valid token yet, so the token minted by the first probe is memoized per client and reused afterwards.
    static async Task<string> XsrfTokenAsync(HttpClient client, string version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/lots");
        request.Headers.Add("X-Inertia", "true");
        request.Headers.Add("X-Inertia-Version", version);
        using var response = await client.SendAsync(request);
        var boxed = XsrfTokens.GetOrCreateValue(client);
        var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(c => c.StartsWith("XSRF-TOKEN="))
            : null;
        if (setCookie is not null)
            boxed.Value = Uri.UnescapeDataString(setCookie["XSRF-TOKEN=".Length..setCookie.IndexOf(';')]);
        Assert.NotNull(boxed.Value); // the first page load must mint the token
        return boxed.Value;
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
