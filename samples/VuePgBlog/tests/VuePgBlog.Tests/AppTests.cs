using System.Net;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;
using Inertia.Net.Testing;
using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;

namespace VuePgBlog.Tests;

/// AppFactory also sets plain APP_NAME="Plain Name"; an explicitly set App:Name must still win.
public sealed class CanonicalNameFactory : AppFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("App:Name", "Canonical Name");
    }
}

public sealed class ConfigPrecedenceTests(CanonicalNameFactory app) : IClassFixture<CanonicalNameFactory>
{
    [Fact]
    public async Task Explicit_canonical_key_beats_plain_env_name()
    {
        using var response = await app.InertiaGetAsync(app.Client(), "/products");
        await response.AssertInertiaAsync(page => page.Where("appName", "Canonical Name").Etc());
    }
}

public sealed class AppTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Guests_can_browse_the_catalog()
    {
        using var response = await app.InertiaGetAsync(app.Client(), "/products");
        await response.AssertInertiaAsync(page => page
            .Component("products/Index")
            .Has("products.data")
            .Where("filters.q", "")
            .WhereNull("auth.user")
            .Etc());
    }

    [Fact]
    public async Task Plain_env_names_override_config_keys()
    {
        using var response = await app.InertiaGetAsync(app.Client(), "/products");
        await response.AssertInertiaAsync(page => page.Where("appName", "Plain Name").Etc());
    }

    [Fact]
    public async Task Partial_reload_returns_only_the_requested_prop()
    {
        var client = app.Client();
        using var response = await app.InertiaPartialAsync(client, "/products", "products/Index", "products");
        response.EnsureSuccessStatusCode();
        var page = await response.GetInertiaPageAsync();
        Assert.True(page.Prop("products") is not null);
        Assert.True(page.Prop("filters") is null);
    }

    [Fact]
    public async Task Health_endpoint_is_healthy()
    {
        using var response = await app.Client().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_products_render_the_error_page_with_404()
    {
        using var response = await app.InertiaGetAsync(app.Client(), "/products/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await response.AssertInertiaAsync(page => page.Component("Error").Where("status", 404).Etc());
    }

    [Fact]
    public async Task Registration_errors_come_back_as_inertia_errors()
    {
        var client = app.Client();
        using var post = await app.SendAsync(client, "/register", new { name = "X", email = "not-an-email", password = "short", passwordConfirmation = "nope" });
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

        using var page = await app.InertiaGetAsync(client, "/register");
        await page.AssertInertiaAsync(p => p
            .Component("Auth/Register")
            .Has("errors.email")
            .Has("errors.password")
            .Etc());
    }

    [Fact]
    public async Task Unverified_users_are_sent_to_verify_before_buying()
    {
        var seeded = await app.SeedProductAsync("Unverified Buy Probe");
        var (client, _) = await app.SignUpAsync("unverified@example.test", verified: false);
        using var response = await app.InertiaGetAsync(client, $"/buy/{seeded.ProductId}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/email/verify", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Guests_are_sent_to_login_from_the_account_pages()
    {
        using var response = await app.InertiaGetAsync(app.Client(), "/account");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Login_and_registration_flows_come_back_to_the_storefront()
    {
        var email = "returning@example.test";
        var (registered, _) = await app.SignUpAsync(email);

        // A signed-in visitor opening /login is sent to the storefront, not shown the form again.
        using var loginPage = await app.InertiaGetAsync(registered, "/login");
        Assert.Equal(HttpStatusCode.Redirect, loginPage.StatusCode);
        Assert.Equal("/products", loginPage.Headers.Location!.PathAndQuery);

        // After logout, a fresh sign-in lands on the storefront too.
        using (await app.SendAsync(registered, "/logout", new { })) { }
        var fresh = app.Client();
        using var login = await app.SendAsync(fresh, "/login", new { email, password = "correct-horse-battery" });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/products", login.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Requests_without_antiforgery_token_are_rejected()
    {
        using var response = await app.Client().PostAsync("/logout", null);
        Assert.Equal((HttpStatusCode)419, response.StatusCode);
    }

    [Fact]
    public async Task Verification_email_job_is_processed_by_the_worker()
    {
        await app.SignUpAsync("jobs@example.test", verified: false);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        JobStatus? status = null;
        while (DateTime.UtcNow < deadline)
        {
            status = await app.QueryAsync(db => db.Jobs.AsNoTracking()
                .Where(j => j.Type == "SendEmail" && j.Payload.Contains("jobs@example.test"))
                .Select(j => (JobStatus?)j.Status).FirstOrDefaultAsync());
            if (status == JobStatus.Done) break;
            await Task.Delay(100);
        }
        Assert.Equal(JobStatus.Done, status);
    }
}
