using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CarParker.Features;
using CarParker.Infrastructure;

namespace CarParker.Tests;

public sealed partial class SecurityTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Html_pages_carry_a_nonce_policy_and_no_https_upgrade_over_http()
    {
        using var response = await app.Client().GetAsync("/login");
        response.EnsureSuccessStatusCode();
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        var nonce = Regex.Match(csp, "script-src[^;]*'nonce-([^']+)'").Groups[1].Value;
        Assert.NotEmpty(nonce);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("http://localhost:5173", csp); // Vite dev server, Development only
        Assert.DoesNotContain("upgrade-insecure-requests", csp);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.False(response.Headers.Contains("Server"));
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));

        // Every script element is either nonce-stamped or the bare theme script allowed by its hash.
        var html = await response.Content.ReadAsStringAsync();
        foreach (Match tag in ScriptTag().Matches(html))
            Assert.True(tag.Value.Contains($"nonce=\"{nonce}\"") || tag.Value == "<script>"
                || tag.Value.Contains("type=\"application/json\""), tag.Value); // JSON data blocks never execute
        Assert.Contains("'sha256-", csp);
    }

    [Fact]
    public async Task Production_over_https_adds_hsts_and_https_upgrade_but_plain_http_does_not()
    {
        await using var host = await StartAsync(Environments.Production);
        var https = host.GetTestClient();
        https.BaseAddress = new Uri("https://example.test");
        using var secure = await https.GetAsync("/");
        var csp = secure.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("upgrade-insecure-requests", csp);
        Assert.DoesNotContain("localhost:5173", csp);
        Assert.Contains("https://files.example.test", csp); // storage origin from config reaches img-src
        Assert.Contains("max-age=31536000", secure.Headers.GetValues("Strict-Transport-Security").Single());

        var http = host.GetTestClient();
        http.BaseAddress = new Uri("http://example.test");
        using var plain = await http.GetAsync("/");
        Assert.DoesNotContain("upgrade-insecure-requests", plain.Headers.GetValues("Content-Security-Policy").Single());
        Assert.False(plain.Headers.Contains("Strict-Transport-Security"));
    }

    static async Task<WebApplication> StartAsync(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment, ContentRootPath = Path.GetTempPath() });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:ServiceUrl"] = "https://files.example.test",
            ["Storage:Bucket"] = "uploads",
        });
        builder.Services.AddSingleton<Storage>();
        var host = builder.Build();
        host.UseMiddleware<SecurityHeadersMiddleware>();
        host.Run(async ctx =>
        {
            ctx.Response.ContentType = "text/html";
            await ctx.Response.WriteAsync("<html></html>");
        });
        await host.StartAsync();
        return host;
    }

    [Fact]
    public async Task Upload_presign_rejects_pdfs_and_oversized_images()
    {
        var (client, _) = await app.SeedUserAsync();
        using var pdf = await app.SendAsync(client, "/uploads/presign", new { contentType = "application/pdf", size = 1024, fileName = "a.pdf" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, pdf.StatusCode);
        using var big = await app.SendAsync(client, "/uploads/presign", new { contentType = "image/png", size = 6 * 1024 * 1024, fileName = "a.png" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, big.StatusCode);
    }

    [Theory]
    [InlineData("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 }, true)]
    [InlineData("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]
    [InlineData("image/gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, true)]
    [InlineData("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, true)]
    [InlineData("image/png", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, false)] // jpeg bytes declared as png
    [InlineData("image/png", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, false)] // a PDF
    [InlineData("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45 }, false)] // RIFF/WAVE
    [InlineData("application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, false)] // type no longer allowed
    [InlineData("image/png", new byte[0], false)]
    public void Magic_bytes_must_match_the_declared_image_type(string contentType, byte[] head, bool expected) =>
        Assert.Equal(expected, UploadsValidators.MatchesSignature(contentType, head));

    [Fact]
    public void Only_admin_and_guard_role_claims_join_the_staff_group()
    {
        static ClaimsPrincipal With(string role) => new(new ClaimsIdentity([new Claim(AuthHelpers.RoleClaim, role)], "test"));
        Assert.True(ParkHub.IsStaff(With("admin")));
        Assert.True(ParkHub.IsStaff(With("guard")));
        Assert.False(ParkHub.IsStaff(With("customer")));
        // The built-in role claim type is not what the auth code issues, so it must not grant anything.
        Assert.False(ParkHub.IsStaff(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "admin")], "test"))));
        Assert.False(ParkHub.IsStaff(new ClaimsPrincipal()));
    }

    [GeneratedRegex("<script[^>]*>")]
    private static partial Regex ScriptTag();
}
