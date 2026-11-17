using System.Net;
using CarParker.Data;

namespace CarParker.Tests;

/// After signing in, each role lands on its own home unless a safe local returnUrl was requested.
/// Own fixture: the /login rate limit allows 10 attempts per minute per IP.
public sealed class LoginRedirectTests(AppFactory app) : IClassFixture<AppFactory>
{
    async Task<string> LoginLocationAsync(string email, string? returnUrl = null)
    {
        using var response = await app.SendAsync(app.Client(), "/login", new { email, password = AppFactory.Password, returnUrl });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.OriginalString;
    }

    [Theory]
    [InlineData(UserRole.Guard, "/guard")]
    [InlineData(UserRole.Admin, "/admin")]
    [InlineData(UserRole.Customer, "/lots")]
    public async Task Login_without_returnUrl_lands_on_the_role_home(UserRole role, string expected)
    {
        var email = $"login-{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        await app.SeedUserAsync(role, email);
        Assert.Equal(expected, await LoginLocationAsync(email));
    }

    [Fact]
    public async Task Login_honours_a_local_returnUrl_and_ignores_an_external_one()
    {
        var email = $"login-return-{Guid.NewGuid():N}@example.test";
        await app.SeedUserAsync(UserRole.Guard, email);
        Assert.Equal("/account/bookings", await LoginLocationAsync(email, "/account/bookings"));
        Assert.Equal("/guard", await LoginLocationAsync(email, "//evil.example/phish"));
    }
}
