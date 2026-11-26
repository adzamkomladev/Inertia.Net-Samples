using System.Net;
using System.Net.Http.Json;
using Inertia.Net.Testing;
using KitchenSink.Data;

namespace KitchenSink.Tests;

[Collection("App")]
public sealed class AuthenticationTests(AppFactory app)
{
    [Fact]
    public async Task Login_screen_renders()
    {
        var client = app.Client();
        var page = await app.PageAsync(client, "/login");

        Assert.Equal("Auth/Login", page.Component);
    }

    [Fact]
    public async Task Users_can_authenticate_from_the_login_screen()
    {
        var client = app.Client();
        using var login = await client.GetAsync("/login");
        using var response = await client.InertiaRequestAsync(
            HttpMethod.Post,
            "/login",
            JsonContent.Create(new { email = DemoSeeder.DemoEmail, password = DemoSeeder.DemoPassword }),
            AppFactory.Version,
            request => request.WithXsrfToken(login));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/dashboard", LocationPath(response));

        var page = await app.PageAsync(client, "/dashboard");
        Assert.Equal("Crm/Dashboard", page.Component);
    }

    [Fact]
    public async Task Invalid_credentials_are_rejected_with_a_message()
    {
        var client = app.Client();
        using var response = await app.PostAsync(
            client,
            "/login",
            new { email = "invalid@example.com", password = "wrong-password" },
            tokenPage: "/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var page = await app.PageAsync(client, "/login");
        new AssertableInertia(page)
            .HasError("email", "These credentials do not match our records.");
    }

    [Fact]
    public async Task Users_can_log_out()
    {
        var client = await app.SignInAsync();

        using var response = await app.PostAsync(client, "/logout");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", LocationPath(response));

        using var after = await client.InertiaGetAsync("/dashboard", AppFactory.Version);
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.Equal("/login", LocationPath(after));
    }

    [Fact]
    public async Task Login_attempts_are_rate_limited()
    {
        var client = app.Client();
        var email = $"throttle-{Guid.NewGuid():N}@example.com";
        HttpResponseMessage? last = null;

        for (var i = 0; i < 6; i++)
        {
            last?.Dispose();
            last = await app.PostAsync(client, "/login", new { email, password = "wrong-password" }, tokenPage: "/login");
        }

        Assert.NotNull(last);
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        last.Dispose();
    }

    [Fact]
    public async Task Home_redirects_to_login()
    {
        var client = app.Client();
        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", LocationPath(response));
    }

    static string LocationPath(HttpResponseMessage response)
    {
        var location = response.Headers.Location ?? throw new InvalidOperationException("The response has no Location header.");
        return location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0];
    }
}
