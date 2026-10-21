using System.Net;
using Inertia.Net.Testing;
using MongoDB.Driver;
using SvelteMongoBlog.Features.Bookings;
using SvelteMongoBlog.Features.Insurance;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Features.Lots;

namespace SvelteMongoBlog.Tests;

public sealed class BookingFlowTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Single_booking_claims_the_spot_and_materializes_window_and_payment()
    {
        var (client, userId) = await app.SharedUserAsync();
        var (lot, spots) = await app.SeedLotAsync("Booking deck");
        var start = DateTime.UtcNow.AddHours(2);

        using var created = await app.BookAsync(client, lot, spots[0], start, start.AddHours(2));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var bookingUrl = created.Headers.Location!.OriginalString;
        Assert.StartsWith("/account/bookings/", bookingUrl);

        var reservation = await app.ReservationAsync(AppFactory.BookingIdOf(created));
        Assert.NotNull(reservation);
        Assert.Equal(userId, reservation!.UserId);
        Assert.Equal(WindowType.Single, reservation.WindowType);
        Assert.Equal(1, reservation.OccurrenceCount);
        Assert.Equal(800, reservation.ParkingCents); // 2h × $4/h, no cap hit
        Assert.Equal(800, reservation.TotalCents);
        Assert.Equal(0, reservation.InsuranceCents);

        var occurrence = await app.QueryAsync(db => db.Occurrences.Find(o => o.ReservationId == reservation.Id).SingleAsync());
        Assert.Equal(OccurrenceStatus.Scheduled, occurrence.Status);
        var payment = await app.QueryAsync(db => db.Payments.Find(p => p.ReservationId == reservation.Id).SingleAsync());
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(800, payment.AmountCents);
        Assert.Equal(SpotState.Booked, await app.QueryAsync(db => db.Spots.Find(s => s.Id == spots[0].Id).Project(s => s.State).SingleAsync()));

        using var page = await app.InertiaGetAsync(client, bookingUrl);
        await page.AssertInertiaAsync(p => p
            .Component("account/Booking")
            .Where("booking.status", "confirmed")
            .Has("booking.occurrences")
            .Etc());
    }

    [Fact]
    public async Task Recurring_booking_materializes_each_weekly_window()
    {
        var (client, _) = await app.SharedUserAsync();
        var (lot, spots) = await app.SeedLotAsync("Recurring deck");

        using var created = await app.BookRecurringAsync(client, lot, spots[0], "1,3", "09:00", "11:00", weeks: 2);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var reservation = await app.ReservationAsync(AppFactory.BookingIdOf(created));

        Assert.NotNull(reservation);
        Assert.Equal(WindowType.Recurring, reservation!.WindowType);
        var expected = Enumerable.Range(1, 14).Select(d => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(d))
            .Count(day => day.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday);
        Assert.Equal(expected, reservation.OccurrenceCount);
        Assert.NotNull(reservation.Pattern);
        Assert.Equal(new[] { 1, 3 }, reservation.Pattern!.DaysOfWeek);
        Assert.Equal(2, reservation.Pattern.Weeks);

        var occurrences = await app.QueryAsync(db => db.Occurrences.Find(o => o.ReservationId == reservation.Id).SortBy(o => o.Index).ToListAsync());
        Assert.Equal(expected, occurrences.Count);
        Assert.All(occurrences, o => Assert.Equal(OccurrenceStatus.Scheduled, o.Status));
        Assert.All(occurrences, o => Assert.Equal(TimeSpan.FromHours(2), o.EndAtUtc - o.StartAtUtc));
        Assert.All(occurrences, o => Assert.True(o.StartAtUtc > DateTime.UtcNow)); // weekly windows start tomorrow
    }

    [Fact]
    public async Task Pricing_caps_long_windows_at_the_daily_max_and_bills_spot_cover_upfront()
    {
        var (client, _) = await app.SharedUserAsync();
        var (lot, spots) = await app.SeedLotAsync("Capped deck", ratePerHourCents: 400, dailyMaxCents: 2400);

        var start = DateTime.UtcNow.AddHours(2);
        using var created = await app.BookAsync(client, lot, spots[0], start, start.AddHours(10), insurance: true);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var reservation = await app.ReservationAsync(AppFactory.BookingIdOf(created));

        Assert.NotNull(reservation);
        Assert.Equal(2400, reservation!.ParkingCents); // 10 × $4 = $40, capped at the $24 daily max
        Assert.Equal(288, reservation.InsuranceCents); // 12% of $24
        Assert.Equal(2688, reservation.TotalCents);
        Assert.NotNull(reservation.InsuranceId);

        var policy = await app.QueryAsync(db => db.SpotCoverPolicies.Find(p => p.Id == reservation.InsuranceId).SingleAsync());
        Assert.Equal(PolicyStatus.Active, policy.Status);
        Assert.Equal(288, policy.PremiumCents);
        Assert.Equal(lot.CoverageCents, policy.CoverageCents);
        Assert.Equal(reservation.Id, policy.ReservationId);
    }

    [Fact]
    public async Task Only_one_concurrent_booking_can_claim_a_spot_and_overlaps_are_refused()
    {
        var (first, _) = await app.SeedUserAsync();
        var (second, _) = await app.SeedUserAsync();
        var (lot, spots) = await app.SeedLotAsync("Contested deck", spotCount: 1);
        var spot = spots[0];
        var start = DateTime.UtcNow.AddHours(2);

        var responses = await Task.WhenAll(
            app.BookAsync(first, lot, spot, start, start.AddHours(2)),
            app.BookAsync(second, lot, spot, start, start.AddHours(2)));

        // The conditional spot update referees the race: exactly one confirmed booking may exist.
        Assert.Equal(1, await app.QueryAsync(db => db.Reservations.CountDocumentsAsync(r => r.SpotId == spot.Id && r.Status == ReservationStatus.Confirmed)));
        Assert.Single(responses, r => r.Headers.Location?.OriginalString.StartsWith("/account/bookings/") == true);
        Assert.Equal(SpotState.Booked, await app.QueryAsync(db => db.Spots.Find(s => s.Id == spot.Id).Project(s => s.State).SingleAsync()));

        // With the spot artificially released, a second booking over the same window hits the overlap guard.
        await app.QueryAsync(async db =>
        {
            await db.Spots.UpdateOneAsync(s => s.Id == spot.Id, Builders<Spot>.Update.Set(s => s.State, SpotState.Free));
            return true;
        });
        using var overlap = await app.BookAsync(second, lot, spot, start.AddMinutes(30), start.AddHours(1));
        Assert.Equal(HttpStatusCode.Redirect, overlap.StatusCode); // back with errors, not to a booking page
        Assert.False(overlap.Headers.Location?.OriginalString.StartsWith("/account/bookings/"));
        Assert.Equal(1, await app.QueryAsync(db => db.Reservations.CountDocumentsAsync(r => r.SpotId == spot.Id)));
    }

    [Theory]
    [InlineData(30, "24h_full", 800)]
    [InlineData(10, "4h_half", 400)]
    [InlineData(2, null, 0)]
    public async Task Cancellation_refund_follows_the_rule_matrix(int hoursOut, string? expectedRule, long expectedCents)
    {
        var (client, userId) = await app.SharedUserAsync();
        var (lot, spots) = await app.SeedLotAsync($"Rule deck {hoursOut}", spotCount: 1);
        var start = DateTime.UtcNow.AddHours(hoursOut);
        using var created = await app.BookAsync(client, lot, spots[0], start, start.AddHours(2));
        var reservationId = AppFactory.BookingIdOf(created);
        using (await app.PayBookingAsync(client, reservationId)) { }

        using var cancelled = await app.CancelBookingAsync(client, reservationId);
        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);

        var reservation = await app.ReservationAsync(reservationId);
        Assert.Equal(ReservationStatus.Cancelled, reservation!.Status);
        Assert.Equal(SpotState.Free, await app.QueryAsync(db => db.Spots.Find(s => s.Id == spots[0].Id).Project(s => s.State).SingleAsync()));
        Assert.Equal(0, await app.QueryAsync(db => db.Occurrences.CountDocumentsAsync(o => o.ReservationId == reservationId && o.Status == OccurrenceStatus.Scheduled)));

        var refund = await app.QueryAsync(db => db.Refunds.Find(rf => rf.ReservationId == reservationId).FirstOrDefaultAsync());
        if (expectedRule is null)
            Assert.Null(refund); // late_none: inside 4h, nothing is requested
        else
        {
            Assert.Equal(expectedRule, refund!.Rule);
            Assert.Equal(expectedCents, refund.AmountCents);
            Assert.Equal(RefundStatus.Requested, refund.Status);
        }
        Assert.Equal(1, await app.QueryAsync(db => db.Alerts.CountDocumentsAsync(a => a.UserId == userId && a.Kind == "refund")));
    }
}
