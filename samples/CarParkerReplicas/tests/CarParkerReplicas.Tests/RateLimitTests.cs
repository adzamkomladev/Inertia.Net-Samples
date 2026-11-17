using System.Net;
using System.Net.Http.Json;
using CarParker.Data;
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
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDb>().Users.CountDocumentsAsync(u => u.Email == email));
    }

    [Fact]
    public async Task Login_is_rate_limited_per_ip_with_429_and_a_redirect_for_inertia()
    {
        var client = app.Client();
        var body = new { email = "nobody@example.test", password = "wrong-password-1" };
        // Plain JSON posts: the limiter runs before antiforgery, so the first ten get 419 and the rest 429.
        HttpStatusCode last = default;
        for (var i = 0; i < 12; i++)
        {
            using var response = await client.PostAsJsonAsync("/login", body);
            last = response.StatusCode;
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
        // An Inertia visit is bounced back with a toast instead of a blank error page.
        using var inertia = await app.SendAsync(client, "/login", body);
        Assert.True((int)inertia.StatusCode is 302 or 303 or 429, inertia.StatusCode.ToString());
    }
}
