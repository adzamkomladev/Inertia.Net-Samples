using System.Net;
using Microsoft.Extensions.Configuration;
using Inertia.Net.Testing;
using MongoDB.Driver;
using CarParker.Data;

namespace CarParker.Tests;

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
        using var response = await app.InertiaGetAsync(app.Client(), "/lots");
        await response.AssertInertiaAsync(page => page.Where("appName", "Canonical Name").Etc());
    }
}

public sealed class AppTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Guests_can_browse_lots()
    {
        var (lot, _) = await app.SeedLotAsync("Public browse deck");
        using var response = await app.InertiaGetAsync(app.Client(), "/lots");
        await response.AssertInertiaAsync(page => page
            .Component("lots/Index")
            .Has("lots.data")
            .Where("filters.q", "")
            .WhereNull("auth.user")
            .Etc());
        var page = await response.GetInertiaPageAsync();
        Assert.Contains(lot.Id, page.Prop("lots.data")!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()));
    }

    [Fact]
    public async Task Plain_env_names_override_config_keys()
    {
        using var response = await app.InertiaGetAsync(app.Client(), "/lots");
        await response.AssertInertiaAsync(page => page.Where("appName", "Plain Name").Etc());
    }

    [Fact]
    public async Task Health_endpoint_is_healthy()
    {
        using var response = await app.Client().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
    public async Task Unverified_users_cannot_open_the_booking_wizard()
    {
        var (client, _) = await app.SignUpAndVerifyAsync("unverified@example.test", verified: false);
        using var response = await app.InertiaGetAsync(client, $"/lots/{Guid.CreateVersion7()}/book");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/email/verify", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Guests_are_sent_to_login_from_protected_pages()
    {
        using var response = await app.InertiaGetAsync(app.Client(), "/account");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location!.PathAndQuery);
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
        await app.SignUpAndVerifyAsync("jobs@example.test", verified: false);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        JobStatus? status = null;
        while (DateTime.UtcNow < deadline)
        {
            status = await app.QueryAsync(async db => (await db.Jobs.Find(j => j.Type == "SendEmail" && j.Payload.Contains("jobs@example.test")).FirstOrDefaultAsync())?.Status);
            if (status == JobStatus.Done) break;
            await Task.Delay(100);
        }
        Assert.Equal(JobStatus.Done, status);
    }

    [Fact]
    public void Presigned_urls_use_the_public_endpoint_while_server_calls_use_the_internal_one()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:ServiceUrl"] = "http://s3:9000",
            ["Storage:PublicServiceUrl"] = "http://localhost:9000",
            ["Storage:Bucket"] = "uploads",
            ["Storage:AccessKey"] = "k",
            ["Storage:SecretKey"] = "s",
            ["Storage:Region"] = "us-east-1",
        }).Build();
        using var storage = new CarParker.Infrastructure.Storage(config);

        var put = new Uri(storage.PresignPut("u/1/x.png", "image/png", TimeSpan.FromMinutes(2)));
        Assert.Equal("localhost", put.Host);
        Assert.Equal(9000, put.Port);
        Assert.StartsWith("/uploads/u/1/x.png", put.AbsolutePath);
        Assert.Equal("localhost", new Uri(storage.Url("u/1/x.png")).Host); // private bucket: presigned GET, also public host
    }
}
