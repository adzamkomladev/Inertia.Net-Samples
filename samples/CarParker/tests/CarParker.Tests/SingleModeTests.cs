using System.Net;
using MongoDB.Driver;
using CarParker.Features.Bookings;
using CarParker.Data;
using CarParker.Features.Lots;

namespace CarParker.Tests;

/// Boots the app in the default single mode (no transactions) against the shared replica-set test server.
public sealed class SingleModeAppFactory : AppFactory
{
    protected override string MongoMode => "single";
}

public sealed class SingleModeTests(SingleModeAppFactory app) : IClassFixture<SingleModeAppFactory>
{
    [Fact]
    public async Task Booking_and_payment_flows_work_without_transactions()
    {
        var (client, _) = await app.SignUpAndVerifyAsync("single-mode@example.test");
        var (lot, spots) = await app.SeedLotAsync("Single mode deck");

        var start = DateTime.UtcNow.AddHours(3);
        using var created = await app.BookAsync(client, lot, spots[0], start, start.AddHours(2));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var reservation = await app.QueryAsync(db => db.Reservations.Find(_ => true).SingleAsync());
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Equal(800, reservation.ParkingCents); // 2h × $4/h
        Assert.Equal(SpotState.Booked, await app.QueryAsync(db => db.Spots.Find(s => s.Id == spots[0].Id).Project(s => s.State).SingleAsync()));

        using var paid = await app.PayBookingAsync(client, reservation.Id);
        Assert.Equal(HttpStatusCode.Redirect, paid.StatusCode);
        Assert.Equal(PaymentStatus.Paid, await app.QueryAsync(db => db.Payments.Find(p => p.ReservationId == reservation.Id).Project(p => p.Status).SingleAsync()));
    }
}
