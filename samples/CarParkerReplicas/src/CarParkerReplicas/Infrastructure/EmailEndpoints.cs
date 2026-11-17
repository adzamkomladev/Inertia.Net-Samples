namespace CarParker.Infrastructure;

/// <summary>Sample data for every template: the Development preview and the tests render from it.</summary>
public static class EmailSamples
{
    public const string BaseUrl = "https://parking.example.com";

    public static IReadOnlyDictionary<string, EmailMessage> All()
    {
        const string to = "ada@example.com";
        const string name = "Ada Lovelace";
        var c = new EmailContext("CarParker", BaseUrl);
        var booking = Guid.Parse("018f3a52-7c1e-7b2a-9d41-3a5f6c7d8e90");
        var start = new DateTime(2026, 11, 20, 13, 30, 0, DateTimeKind.Utc);
        var bookingUrl = c.Url($"/account/bookings/{booking}");

        return new Dictionary<string, EmailMessage>
        {
            ["verify"] = Emails.Verify(c, to, name, c.Url("/email/verify/confirm?token=sample")),
            ["booking-confirmed"] = Emails.BookingConfirmed(c, to, name, "Harbour Street Deck", "B-14",
                CarParker.Features.Lots.LotTime.Span(start, start.AddHours(4), "America/New_York"), 1_450, "paid", bookingUrl, c.Url($"/account/bookings/{booking}/receipt.pdf")),
            ["session-ended"] = Emails.SessionEnded(c, to, name, "Harbour Street Deck", start.AddHours(3), bookingUrl, c.Url($"/account/bookings/{booking}/receipt.pdf"), "America/New_York"),
            ["password-reset"] = Emails.PasswordReset(c, to, name, c.Url("/reset-password?token=sample")),
            ["no-show"] = Emails.NoShow(c, to, name, "Harbour Street Deck", "B-14",
                CarParker.Features.Lots.LotTime.Span(start, start.AddHours(2), "America/New_York"), 30, 725, bookingUrl),
            ["overstay"] = Emails.Overstay(c, to, name, "Harbour Street Deck", "B-14",
                CarParker.Features.Lots.LotTime.Format(start.AddHours(2), "America/New_York"), "+1 212 555 0148", bookingUrl),
            ["reservation-reminder"] = Emails.ReservationReminder(c, to, name, "Harbour Street Deck", "B-14", start, bookingUrl, "America/New_York"),
            ["refund-paid"] = Emails.RefundPaid(c, to, name, 1_250, booking, c.Url("/account/payments"), c.Url($"/account/bookings/{booking}/refund-receipt.pdf")),
            ["claim-approved"] = Emails.ClaimDecision(c, to, name, "damage", true, 18_500, "Photos matched the guard's incident report.", c.Url("/account/insurance")),
            ["claim-rejected"] = Emails.ClaimDecision(c, to, name, "theft", false, null, "The incident was outside the booked window.", c.Url("/account/insurance")),
        };
    }
}

/// <summary>Development-only gallery of every email template with sample data. Not mapped in any other environment.</summary>
public static class EmailEndpoints
{
    public static void MapEmailPreviews(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        app.MapGet("/dev/emails", () => Results.Content(
            "<!doctype html><meta charset=utf-8><title>Email previews</title><body style=\"font:16px/1.9 system-ui;padding:24px\"><h1>Email previews</h1><ul>"
            + string.Concat(EmailSamples.All().Keys.Select(k => $"<li><a href=\"/dev/emails/{k}\">{k}</a> · <a href=\"/dev/emails/{k}?format=text\">text</a></li>"))
            + "</ul></body>", "text/html; charset=utf-8")).AllowAnonymous();

        app.MapGet("/dev/emails/{name}", (string name, string? format) =>
            EmailSamples.All().TryGetValue(name, out var mail)
                ? format == "text"
                    ? Results.Text($"Subject: {mail.Subject}\n\n{mail.Text}", "text/plain; charset=utf-8")
                    : Results.Content(mail.Html, "text/html; charset=utf-8")
                : Results.NotFound()).AllowAnonymous();
    }
}
