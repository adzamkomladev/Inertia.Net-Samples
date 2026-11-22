using System.Net;
using System.Net.Http.Json;
using CarParker.Data;
using CarParker.Features;
using Inertia.Net.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace CarParker.Tests;

/// Own fixture: the auth window is per client IP, which every TestServer client shares.
public sealed class RateLimitTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Precognition_field_checks_do_not_spend_the_registration_attempts()
    {
        var client = app.Client();
        const string email = "precog@example.test";
        var body = new { name = "Precog User", email, password = AppFactory.Password, passwordConfirmation = AppFactory.Password };
        // Validating each field on change used to share the 10/minute auth window with the real submit.
        for (var i = 0; i < 12; i++)
        {
            using var check = await app.SendAsync(client, "/register", body, precognition: true);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, check.StatusCode);
        }
        using var response = await app.SendAsync(client, "/register", body);
        // Customer logins render the lots page inline; registration renders the verify page inline. The account exists either way.
        var landed = response.Headers.Location?.OriginalString ?? (await response.GetInertiaPageAsync()).Url;
        Assert.Equal("/email/verify", landed);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDb>().Users.CountDocumentsAsync(u => u.Email == email));
    }

    [Fact]
    public async Task Login_is_rate_limited_per_account_when_the_address_is_room_sized()
    {
        var client = app.Client();
        var email = $"guarded-{Guid.NewGuid():N}@example.test";
        // Per-address windows are sized for a whole room behind one NAT (playbook), so the account is the abuse
        // boundary: thirty failed sign-ins per account in the window, then the guard answers instead of the database.
        for (var i = 0; i < AuthService.AttemptsPerWindow; i++)
        {
            using var attempt = await app.SendAsync(client, "/login", new { email, password = "wrong-password-1" });
            Assert.NotEqual(HttpStatusCode.TooManyRequests, attempt.StatusCode);
        }
        // The guard failure comes back like any validation error: a redirect whose follow-up page carries the errors.
        using var guarded = await app.SendAsync(client, "/login", new { email, password = "wrong-password-1" });
        Assert.True((int)guarded.StatusCode is 302 or 303, guarded.StatusCode.ToString());
        using var next = await client.GetAsync(guarded.Headers.Location!.OriginalString);
        var page = await next.GetInertiaPageAsync();
        Assert.Contains("Too many sign-in attempts", page.Prop("errors.email")!.GetValue<string>());
    }
}
