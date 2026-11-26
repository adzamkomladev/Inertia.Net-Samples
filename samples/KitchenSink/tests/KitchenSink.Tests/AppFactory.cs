using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inertia.Net;
using Inertia.Net.Testing;
using KitchenSink.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace KitchenSink.Tests;

/// <summary>Boots the real app against a throwaway SQLite file with the demo data seeded.</summary>
public class AppFactory : WebApplicationFactory<Program>
{
    public const string Version = "test";

    readonly string _dir = Path.Combine(Path.GetTempPath(), "ks-tests-" + Guid.NewGuid().ToString("N"));

    protected virtual bool GlobalRateLimit => false;
    protected virtual int GlobalRateLimitPerMinute => 120;
    protected virtual int PartialRateLimit => 600;
    protected virtual bool QueryMethod => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dir);
        builder.UseEnvironment("Development");
        builder.UseSetting("App:DataDir", _dir);
        builder.UseSetting("DATABASE_PATH", Path.Combine(_dir, "test.db"));
        builder.UseSetting("Demo:GlobalRateLimit", GlobalRateLimit ? "true" : "false");
        builder.UseSetting("Demo:GlobalRateLimitPerMinute", GlobalRateLimitPerMinute.ToString());
        builder.UseSetting("Demo:PartialRateLimit", PartialRateLimit.ToString());
        builder.UseSetting("Demo:QueryMethod", QueryMethod ? "true" : "false");
        builder.UseSetting("Demo:DailyReset", "false");
        builder.ConfigureTestServices(services =>
        {
            // A stable version means no test needs to read the Vite manifest first.
            services.PostConfigure<InertiaOptions>(o => o.Version = Version);

            // Feature tests assert broadcasts without opening a SignalR connection.
            if (GlobalRateLimit)
            {
                return;
            }

            services.RemoveAll<IInertiaLiveBroadcaster>();
            services.AddSingleton<TestLiveBroadcaster>();
            services.AddSingleton<IInertiaLiveBroadcaster>(sp => sp.GetRequiredService<TestLiveBroadcaster>());
        });
    }

    public TestLiveBroadcaster Live => Services.GetRequiredService<TestLiveBroadcaster>();

    /// <summary>A cookie-keeping client that does not follow redirects, with a page-load bootstrapped antiforgery token.</summary>
    public HttpClient Client()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("http://localhost"),
        });
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html");

        using var page = client.GetAsync("/login").GetAwaiter().GetResult();
        return client;
    }

    public async Task<HttpClient> SignInAsync(string email = DemoSeeder.DemoEmail, string password = DemoSeeder.DemoPassword)
    {
        var client = Client();
        using var login = await client.GetAsync("/login");
        using var response = await client.InertiaRequestAsync(
            HttpMethod.Post,
            "/login",
            JsonContent.Create(new { email, password }),
            Version,
            request => request.WithXsrfToken(login));

        if (response.StatusCode is not (HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.OK))
        {
            throw new InvalidOperationException(
                $"Sign in failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return client;
    }

    public async Task<InertiaPage> PageAsync(HttpClient client, string url, Action<HttpRequestMessage>? configure = null)
    {
        using var response = await client.InertiaGetAsync(url, Version, configure);
        var page = await response.GetInertiaPageAsync();
        Assert.True((int)response.StatusCode is >= 200 and < 400, $"GET {url} returned {(int)response.StatusCode}");
        return page;
    }

    /// <summary>An Inertia form post with a fresh antiforgery token and optional error bag header.</summary>
    public async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string url,
        object? body = null,
        HttpMethod? method = null,
        string? errorBag = null,
        string? tokenPage = null,
        Action<HttpRequestMessage>? configure = null)
    {
        using var page = await client.GetAsync(tokenPage ?? "/dashboard");
        var request = new HttpRequestMessage(method ?? HttpMethod.Post, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.TryAddWithoutValidation("X-Inertia", "true");
        request.Headers.TryAddWithoutValidation("X-Inertia-Version", Version);
        if (errorBag is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Inertia-Error-Bag", errorBag);
        }

        request.WithXsrfToken(page);
        configure?.Invoke(request);
        return await client.SendAsync(request);
    }

    public async Task<T> DbAsync<T>(Func<AppDb, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDb>());
    }

    public async Task DbAsync(Func<AppDb, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDb>());
    }

    public Task ResetCrmAsync() => DbAsync(async db =>
    {
        await db.Notes.ExecuteDeleteAsync();
        await db.Contacts.ExecuteDeleteAsync();
        await db.Organizations.ExecuteDeleteAsync();
        await db.LiveVotes.ExecuteDeleteAsync();
        await db.LiveReactions.ExecuteDeleteAsync();
        await db.LiveTicks.ExecuteDeleteAsync();
    });

    public Task<Organization> SeedOrganizationAsync(string name = "Acme Corp") => DbAsync(async db =>
    {
        var organization = new Organization { Name = name, CreatedAt = DateTime.UtcNow };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        return organization;
    });

    public Task<Contact> SeedContactAsync(
        string firstName = "Alice",
        string lastName = "Wonder",
        string? email = "alice@example.com",
        int? organizationId = null,
        bool favorite = false) => DbAsync(async db =>
    {
        var contact = new Contact
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Phone = "555-1234",
            OrganizationId = organizationId,
            IsFavorite = favorite,
            CreatedAt = DateTime.UtcNow,
        };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();
        return contact;
    });

    public Task<Note> SeedNoteAsync(int contactId, string body = "A test note.", int? userId = null) => DbAsync(async db =>
    {
        var user = userId ?? await db.Users.Select(u => u.Id).FirstAsync();
        var note = new Note
        {
            Body = body,
            ContactId = contactId,
            UserId = user,
            CreatedAt = DateTime.UtcNow,
        };
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        return note;
    });

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        catch (Exception ex) when (ex is ObjectDisposedException
            || (ex is AggregateException aggregate && aggregate.Flatten().InnerExceptions.All(e => e is ObjectDisposedException)))
        {
        }

        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class TestLiveBroadcaster : IInertiaLiveBroadcaster
{
    readonly ConcurrentQueue<LiveMessage> _messages = new();

    public IReadOnlyCollection<LiveMessage> Messages => _messages.ToArray();

    public ValueTask PublishAsync(LiveMessage message, CancellationToken cancellationToken = default)
    {
        _messages.Enqueue(message);
        return ValueTask.CompletedTask;
    }

    public void Clear()
    {
        while (_messages.TryDequeue(out _))
        {
        }
    }

    public JsonElement Payloads(string eventName) =>
        _messages.Where(m => m.Event == eventName).Select(m => m.Payload).LastOrDefault();
}

[CollectionDefinition("App")]
public sealed class AppCollection : ICollectionFixture<AppFactory>;
