using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Features;

namespace TechLandier.Tests;

/// Security headers, the guest complaint form and the Content-Security-Policy nonce.
public sealed partial class SecurityTests(AppFactory app) : IClassFixture<AppFactory>
{
    [GeneratedRegex("""<script[^>]*data-page[^>]*nonce="([^"]+)"|<script[^>]*nonce="([^"]+)"[^>]*data-page""")]
    private static partial Regex PageScriptNonce();

    [Fact]
    public async Task Html_responses_carry_a_csp_whose_nonce_matches_the_page_script()
    {
        var client = app.Client();
        using var response = await client.GetAsync("/products");
        var html = await response.Content.ReadAsStringAsync();

        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        var nonce = Regex.Match(csp, "script-src[^;]*'nonce-([^']+)'").Groups[1].Value;
        Assert.NotEmpty(nonce);
        var match = PageScriptNonce().Match(html);
        Assert.True(match.Success, "the Inertia page-data script has no nonce attribute");
        Assert.Equal(nonce, match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);

        // The one static inline script in app.html is allowed by hash, so the policy never needs 'unsafe-inline' for scripts.
        var root = app.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
        var appHtml = await File.ReadAllTextAsync(Path.Combine(root, "app.html"));
        var inline = Regex.Match(appHtml, "<script>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains($"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(inline)))}'", csp);
        Assert.DoesNotMatch("script-src[^;]*'unsafe-inline'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("https://fonts.googleapis.com", csp);
        Assert.Contains("https://fonts.gstatic.com", csp);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.True(response.Headers.Contains("Permissions-Policy"));
        // Development runs over plain HTTP on localhost: HSTS is production-only.
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));

        // A fresh request gets a fresh nonce.
        using var second = await client.GetAsync("/products");
        Assert.NotEqual(csp, Assert.Single(second.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task Guest_complaint_with_someone_elses_order_number_stays_a_guest_ticket()
    {
        var (_, ownerId) = await app.SignUpAsync("ticket-owner@example.test");
        var number = await SeedOrderAsync(ownerId);
        var guest = app.Client();

        using var response = await app.SendAsync(guest, "/support", new
        {
            name = "Mallory Guest", email = "mallory@example.test", subject = "Where is my order",
            category = "other", message = "Please help with this order.", orderNumber = number,
        });
        Assert.True(response.IsSuccessStatusCode);

        var ticket = await app.QueryAsync(async db => await db.Tickets.AsNoTracking().SingleAsync(t => t.Subject == "Where is my order"));
        Assert.Null(ticket.UserId);
        Assert.Null(ticket.OrderId);
        Assert.Equal("mallory@example.test", ticket.GuestEmail);
    }

    [Fact]
    public async Task Guest_complaint_from_the_orders_own_email_links_that_order()
    {
        var (_, ownerId) = await app.SignUpAsync("ticket-linked@example.test");
        var number = await SeedOrderAsync(ownerId);
        var guest = app.Client();

        using var response = await app.SendAsync(guest, "/support", new
        {
            name = "Order Owner", email = "Ticket-Linked@Example.test", subject = "Owner complaint",
            category = "other", message = "My order has a problem.", orderNumber = number,
        });
        Assert.True(response.IsSuccessStatusCode);

        var ticket = await app.QueryAsync(async db => await db.Tickets.AsNoTracking().SingleAsync(t => t.Subject == "Owner complaint"));
        Assert.Equal(ownerId, ticket.UserId);
        Assert.NotNull(ticket.OrderId);
        Assert.Null(ticket.GuestEmail);
    }

    [Fact]
    public async Task Guest_complaint_fields_are_length_capped()
    {
        var guest = app.Client();
        using var response = await app.SendAsync(guest, "/support", new
        {
            name = "Long Message", email = "long@example.test", subject = "A long one",
            category = "other", message = new string('x', SupportService.Limits.Message + 1),
        });
        Assert.False(await app.QueryAsync(async db => await db.Tickets.AnyAsync(t => t.Subject == "A long one")));
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    async Task<string> SeedOrderAsync(Guid userId)
    {
        var number = "TL-SEC-" + Guid.NewGuid().ToString("N")[..8];
        await app.QueryAsync(async db =>
        {
            db.Orders.Add(new Order { Number = number, UserId = userId, CheckoutAttemptId = Guid.NewGuid().ToString("N") });
            await db.SaveChangesAsync();
            return 0;
        });
        return number;
    }
}

/// Own fixture: the guard window is per reply-to email, so every attempt uses the same address.
public sealed class GuestSupportRateLimitTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Guest_complaints_are_limited_to_five_per_reply_to_email()
    {
        var client = app.Client();
        var token = await app.XsrfAsync(client);
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++)
        {
            // A plain JSON POST (no X-Inertia): the per-address limiter is room-sized, so the per-email service
            // guard, not a 429, stops the sixth complaint.
            using var request = new HttpRequestMessage(HttpMethod.Post, "/support")
            {
                Content = JsonContent.Create(new { name = "Spam Bot", email = "spam@example.test", subject = $"Spam {i}", category = "other", message = "hello there" }),
            };
            request.Headers.Add("X-XSRF-TOKEN", token);
            using var response = await client.SendAsync(request);
            statuses.Add(response.StatusCode);
        }
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, statuses);
        Assert.All(statuses, s => Assert.InRange((int)s, 200, 399));
        // Five complaints were accepted; the sixth and seventh were refused by the per-email guard.
        Assert.Equal(SupportService.GuestComplaintsPerWindow, await app.QueryAsync(async db => await db.Tickets.CountAsync(t => t.GuestEmail == "spam@example.test")));
    }
}
