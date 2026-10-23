using System.Net;
using Microsoft.EntityFrameworkCore;

namespace TechLandier.Tests;

/// Own fixture: the auth window is per client IP, which every TestServer client shares.
public sealed class RateLimitTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Precognition_field_checks_do_not_spend_the_registration_attempts()
    {
        var client = app.Client();
        var body = new { name = "Precog User", email = "precog@example.test", password = "correct-horse-battery", passwordConfirmation = "correct-horse-battery" };
        // Validating each field on change used to share the 10/minute auth window with the real submit.
        for (var i = 0; i < 12; i++)
        {
            using var check = await app.SendAsync(client, "/register", body, precognition: true);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, check.StatusCode);
        }
        using (await app.SendAsync(client, "/register", body)) { }
        Assert.True(await app.QueryAsync(async db => await db.Users.AnyAsync(u => u.Email == "precog@example.test")));
    }
}
