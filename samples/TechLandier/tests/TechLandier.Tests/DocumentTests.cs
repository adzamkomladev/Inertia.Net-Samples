using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechLandier.Data;
using TechLandier.Features;
using TechLandier.Infrastructure;

namespace TechLandier.Tests;

/// PDF receipts and statements, the email templates, and the unsubscribe flow.
public sealed class DocumentTests(AppFactory app) : IClassFixture<AppFactory>
{
    const string Probe = "<script>alert('x')</script>";

    static object Checkout(AppFactory.SeededProduct p, int? bnpl = null, string method = "card") => new
    {
        productId = p.ProductId, qty = 1, zoneId = p.ZoneId, paymentMethod = method, recipientName = "Ama Mensah", phone = "+233 24 000 0001",
        streetLabel = "GA-183-5287 Nyaneba Avenue", insurance = false, bnplCount = bnpl, checkoutAttemptId = Guid.NewGuid().ToString(),
    };

    async Task<Order> OrderOfAsync(Guid uid) =>
        await app.QueryAsync(async db => await db.Orders.AsNoTracking().OrderByDescending(o => o.CreatedAt).FirstAsync(o => o.UserId == uid));

    static async Task AssertPdfAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public async Task Receipt_is_for_the_owner_of_a_paid_order_and_nobody_else()
    {
        var p = await app.SeedProductAsync("Receipt Phone", price: 120m, stock: 5);
        var (owner, uid) = await app.SignUpAsync("receipt-owner@example.test");
        var (other, _) = await app.SignUpAsync("receipt-other@example.test");
        using (await app.SendAsync(owner, "/checkout", Checkout(p))) { }
        var order = await OrderOfAsync(uid);
        Assert.Equal(PaymentStatuses.Paid, order.PaymentStatus); // Development settles card payments at once

        using (var ok = await owner.GetAsync($"/account/orders/{order.Number}/receipt.pdf")) await AssertPdfAsync(ok);
        using (var stranger = await other.GetAsync($"/account/orders/{order.Number}/receipt.pdf")) Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);

        // Guests hold the order's access token, exactly like the tracking page.
        using var guest = app.Client();
        using (var withToken = await guest.GetAsync($"/orders/{order.Number}/receipt.pdf?t={Uri.EscapeDataString(order.AccessToken)}")) await AssertPdfAsync(withToken);
        using (var noToken = await guest.GetAsync($"/orders/{order.Number}/receipt.pdf")) Assert.Equal(HttpStatusCode.NotFound, noToken.StatusCode);
        using (var wrongToken = await guest.GetAsync($"/orders/{order.Number}/receipt.pdf?t=nope")) Assert.Equal(HttpStatusCode.NotFound, wrongToken.StatusCode);

        var (admin, _) = await app.AdminAsync("receipt-admin@example.test");
        using (var adminPdf = await admin.GetAsync($"/admin/orders/{order.Number}/receipt.pdf")) await AssertPdfAsync(adminPdf);
        using (var customerOnAdmin = await owner.GetAsync($"/admin/orders/{order.Number}/receipt.pdf")) Assert.NotEqual("application/pdf", customerOnAdmin.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Receipt_is_missing_until_the_order_is_paid()
    {
        var p = await app.SeedProductAsync("Unpaid Phone", price: 60m, stock: 5);
        var (client, uid) = await app.SignUpAsync("receipt-unpaid@example.test");
        using (await app.SendAsync(client, "/checkout", Checkout(p))) { }
        var order = await OrderOfAsync(uid);
        await app.QueryAsync(async db => await db.Orders.Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentStatus, PaymentStatuses.Unpaid).SetProperty(o => o.Status, OrderStatuses.Pending)));

        using var response = await client.GetAsync($"/account/orders/{order.Number}/receipt.pdf");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var guest = await app.Client().GetAsync($"/orders/{order.Number}/receipt.pdf?t={Uri.EscapeDataString(order.AccessToken)}");
        Assert.Equal(HttpStatusCode.NotFound, guest.StatusCode);
    }

    [Fact]
    public async Task Statement_is_for_the_plan_owner_only()
    {
        var p = await app.SeedProductAsync("Statement Phone", price: 90m, stock: 5);
        var (owner, uid) = await app.SignUpAsync("statement-owner@example.test");
        var (other, _) = await app.SignUpAsync("statement-other@example.test");
        using (await app.SendAsync(owner, "/checkout", Checkout(p, bnpl: 3))) { }
        var planId = await app.QueryAsync(async db => await db.PayPlans.Where(x => x.UserId == uid).Select(x => x.Id).SingleAsync());

        using (var ok = await owner.GetAsync($"/account/installments/{planId}/statement.pdf")) await AssertPdfAsync(ok);
        using (var stranger = await other.GetAsync($"/account/installments/{planId}/statement.pdf")) Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        using (var missing = await owner.GetAsync($"/account/installments/{Guid.NewGuid()}/statement.pdf")) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Statement_totals_leave_waived_installments_out()
    {
        var due = DateOnly.FromDateTime(DateTime.UtcNow);
        var data = new StatementData(StoreInfo.Sample, Guid.NewGuid(), "TL-1", "Ama", "ama@example.test", PayPlanStatuses.Active, DateTime.UtcNow, null, 4, 100m, 10m,
        [
            new(1, 100m, due, InstallmentStatuses.Paid, DateTime.UtcNow),
            new(2, 100m, due, InstallmentStatuses.Waived, null),
            new(3, 100m, due, InstallmentStatuses.Overdue, null),
            new(4, 100m, due, InstallmentStatuses.Due, null),
        ]);
        Assert.Equal((300m, 100m, 200m, 100m, 100m), (data.PlanTotal, data.Paid, data.Remaining, data.Waived, data.Overdue));
        Assert.Equal(3, data.Next!.Number);
    }

    [Fact]
    public void Every_email_template_renders_and_escapes_what_customers_type()
    {
        var all = EmailSamples.All(Probe);
        Assert.Equal(12, all.Count);
        foreach (var (name, mail) in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(mail.Subject), name);
            Assert.False(string.IsNullOrWhiteSpace(mail.Text), $"{name} has no plain-text twin");
            Assert.DoesNotContain("<script", mail.Html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("&lt;script&gt;", mail.Html);
            Assert.Contains("color-scheme", mail.Html);
            Assert.Contains("prefers-color-scheme", mail.Html);
        }
        // Verify and receipts are transactional; installment notices carry the one-click unsubscribe link.
        Assert.Null(all["verify"].UnsubscribeUrl);
        Assert.Null(all["order-paid"].UnsubscribeUrl);
        foreach (var name in new[] { "installment-paid", "installment-reminder", "installment-overdue", "installment-statement" })
        {
            Assert.NotNull(all[name].UnsubscribeUrl);
            Assert.Contains("Unsubscribe", all[name].Html);
        }
        Assert.Contains("Download receipt (PDF)", all["order-paid"].Html);
        Assert.Contains("Download statement (PDF)", all["installment-statement"].Html);
        Assert.Contains("firmware fix", all["ticket-reply"].Text);
    }

    [Fact]
    public async Task Dev_email_gallery_renders_in_development()
    {
        using var client = app.Client();
        using var index = await client.GetAsync("/dev/emails");
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        using var html = await client.GetAsync("/dev/emails/order-paid");
        Assert.Contains("Download receipt (PDF)", await html.Content.ReadAsStringAsync());
        using var text = await client.GetAsync("/dev/emails/order-paid?format=text");
        Assert.StartsWith("Subject:", await text.Content.ReadAsStringAsync());
        using var unknown = await client.GetAsync("/dev/emails/nope");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Dev_email_gallery_does_not_exist_outside_development()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        await using var production = builder.Build();
        production.MapEmailEndpoints();
        await production.StartAsync();
        using var client = production.GetTestClient();
        using var response = await client.GetAsync("/dev/emails");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var one = await client.GetAsync("/dev/emails/verify");
        Assert.Equal(HttpStatusCode.NotFound, one.StatusCode);
        await production.StopAsync();
    }

    [Fact]
    public async Task Unsubscribe_link_asks_on_get_and_switches_the_preference_off_on_post()
    {
        var (_, uid) = await app.SignUpAsync("unsub@example.test");
        string url;
        await using (var scope = app.Services.CreateAsyncScope())
            url = scope.ServiceProvider.GetRequiredService<EmailUnsubscribe>().Url(uid, EmailKind.InstallmentReminders);
        using var client = app.Client();

        using (var page = await client.GetAsync(url)) Assert.Contains("Unsubscribe", await page.Content.ReadAsStringAsync());
        Assert.True(await Prefs(uid)); // GET alone changes nothing: link scanners prefetch

        using (var oneClick = await client.PostAsync(url, new FormUrlEncodedContent([new("List-Unsubscribe", "One-Click")])))
            Assert.Equal(HttpStatusCode.OK, oneClick.StatusCode);
        Assert.False(await Prefs(uid));

        using var forged = await client.PostAsync("/email/unsubscribe?token=forged", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);

        async Task<bool> Prefs(Guid id) => await app.QueryAsync(async db =>
            (await db.Users.AsNoTracking().SingleAsync(u => u.Id == id)).Preferences.EmailInstallmentReminders);
    }
}
