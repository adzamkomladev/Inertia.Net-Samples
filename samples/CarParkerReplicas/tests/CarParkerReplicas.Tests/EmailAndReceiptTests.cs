using System.Net;
using System.Text.RegularExpressions;
using CarParker.Infrastructure;

namespace CarParker.Tests;

public sealed class EmailAndReceiptTests(AppFactory app) : IClassFixture<AppFactory>
{
    async Task<Guid> PaidBookingAsync(HttpClient client, int hoursOut = 30, bool cancel = false)
    {
        var (lot, spots) = await app.SeedLotAsync($"Receipt deck {Guid.NewGuid():N}", spotCount: 1);
        var start = DateTime.UtcNow.AddHours(hoursOut);
        using var booking = await app.BookAsync(client, lot, spots[0], start, start.AddHours(2));
        var id = AppFactory.BookingIdOf(booking);
        using (await app.PayBookingAsync(client, id)) { }
        if (cancel) using (await app.CancelBookingAsync(client, id)) { }
        return id;
    }

    [Fact]
    public async Task Owner_downloads_a_booking_receipt_pdf()
    {
        var (client, _) = await app.SharedUserAsync();
        var id = await PaidBookingAsync(client);
        using var response = await client.GetAsync($"/account/bookings/{id}/receipt.pdf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.StartsWith("receipt-", response.Content.Headers.ContentDisposition?.FileName);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public async Task Admin_can_download_and_other_customers_get_403_or_404()
    {
        var (owner, _) = await app.SharedUserAsync();
        var id = await PaidBookingAsync(owner);
        var (admin, _) = await app.SharedUserAsync(CarParker.Data.UserRole.Admin);
        var (stranger, _) = await app.SeedUserAsync();

        using var asAdmin = await admin.GetAsync($"/account/bookings/{id}/receipt.pdf");
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
        using var asStranger = await stranger.GetAsync($"/account/bookings/{id}/receipt.pdf");
        Assert.Equal(HttpStatusCode.Forbidden, asStranger.StatusCode);
        using var strangerRefund = await stranger.GetAsync($"/account/bookings/{id}/refund-receipt.pdf");
        Assert.Equal(HttpStatusCode.Forbidden, strangerRefund.StatusCode);
        using var missing = await owner.GetAsync($"/account/bookings/{Guid.NewGuid()}/receipt.pdf");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Refund_receipt_exists_only_once_a_refund_does()
    {
        var (client, _) = await app.SharedUserAsync();
        var plain = await PaidBookingAsync(client);
        using var none = await client.GetAsync($"/account/bookings/{plain}/refund-receipt.pdf");
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);

        var cancelled = await PaidBookingAsync(client, cancel: true);
        using var refund = await client.GetAsync($"/account/bookings/{cancelled}/refund-receipt.pdf");
        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);
        Assert.Equal("application/pdf", refund.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("refund-receipt-", refund.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(await refund.Content.ReadAsByteArrayAsync(), 0, 4));
    }

    [Fact]
    public void Plain_text_formats_money_as_currency()
    {
        var c = new EmailContext("CarParker", "https://parking.example.com");
        var mail = Emails.RefundPaid(c, "a@example.com", "Ada Lovelace", 123_456, Guid.Parse("01900000-0000-7000-8000-000000000001"), c.Url("/account/payments"), c.Url("/x.pdf"));
        Assert.Contains("$1,234.56", mail.Text);
        Assert.Contains("$1,234.56", mail.Html);
        Assert.DoesNotContain("123456c", mail.Text);
        Assert.DoesNotMatch(@"\d+c\b", mail.Text);
    }

    [Fact]
    public void Every_template_has_absolute_links_a_preheader_and_a_plain_text_twin()
    {
        foreach (var (name, mail) in EmailSamples.All())
        {
            Assert.False(string.IsNullOrWhiteSpace(mail.Text), name);
            Assert.Contains("display:none", mail.Html);
            foreach (Match m in Regex.Matches(mail.Html, "href=\"([^\"]*)\""))
                Assert.True(m.Groups[1].Value.StartsWith("https://") || m.Groups[1].Value.StartsWith("mailto:"), $"{name}: {m.Groups[1].Value}");
            foreach (Match m in Regex.Matches(mail.Text, @"(?<![\w/])(/account\S*)"))
                Assert.Fail($"{name} has a relative link in plain text: {m.Value}");
            Assert.Contains("https://", mail.Text);
        }
    }

    [Fact]
    public async Task Preview_gallery_renders_in_development()
    {
        using var client = app.Client();
        using var index = await client.GetAsync("/dev/emails");
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        using var html = await client.GetAsync("/dev/emails/refund-paid");
        Assert.Contains("$12.50", await html.Content.ReadAsStringAsync());
        using var text = await client.GetAsync("/dev/emails/refund-paid?format=text");
        Assert.StartsWith("text/plain", text.Content.Headers.ContentType?.MediaType ?? "text/plain");
        using var missing = await client.GetAsync("/dev/emails/nope");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
