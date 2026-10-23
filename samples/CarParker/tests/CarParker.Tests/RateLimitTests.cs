using System.Net;
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
}
