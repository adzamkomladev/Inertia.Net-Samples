using MongoDB.Driver;
using SvelteMongoBlog.Features.Bookings;
using SvelteMongoBlog.Features.Lots;
using Microsoft.Extensions.DependencyInjection;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Features.Guard;
using SvelteMongoBlog.Infrastructure;

namespace SvelteMongoBlog.Tests;

public sealed class NoShowTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Sweep_marks_past_uncheckedin_windows_no_show_and_frees_the_spot()
    {
        var (customer, customerId) = await app.SharedUserAsync();
        var (lot, spots) = await app.SeedLotAsync("No-show deck", spotCount: 1);
        using var booking = await app.BookAsync(customer, lot, spots[0], DateTime.UtcNow.AddMinutes(30), DateTime.UtcNow.AddMinutes(90));
        var reservationId = AppFactory.BookingIdOf(booking);
        using (await app.PayBookingAsync(customer, reservationId)) { }

        // Wind the occurrence back past start + 30 min without any check-in.
        await WindBackAsync(app, reservationId, minutesAgo: 45);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        Assert.Equal(1, await GuardService.SweepNoShowsAsync(db, scope.ServiceProvider.GetRequiredService<JobQueue>(),
            scope.ServiceProvider.GetRequiredService<RealtimeEvents>(), CancellationToken.None));

        Assert.Equal(OccurrenceStatus.NoShow, await app.QueryAsync(db => db.Occurrences.Find(o => o.ReservationId == reservationId).Project(o => o.Status).SingleAsync()));
        Assert.Equal(SpotState.Free, await app.QueryAsync(db => db.Spots.Find(s => s.Id == spots[0].Id).Project(s => s.State).SingleAsync()));
        // The window already started, so the rule matrix yields late_none ($0) and no refund is staged.
        Assert.Equal(0, await db.Refunds.CountDocumentsAsync(rf => rf.ReservationId == reservationId));
        Assert.Equal(1, await db.Alerts.CountDocumentsAsync(a => a.UserId == customerId && a.Kind == "gate" && a.Title == "No-show recorded"));
    }

    [Fact]
    public async Task Occurrences_with_a_session_are_never_swept()
    {
        var (customer, customerId) = await app.SharedUserAsync();
        var (lot, spots) = await app.SeedLotAsync("No-show skip deck", spotCount: 1);
        using var booking = await app.BookAsync(customer, lot, spots[0], DateTime.UtcNow.AddMinutes(30), DateTime.UtcNow.AddMinutes(90));
        var reservationId = AppFactory.BookingIdOf(booking);
        var occurrenceId = await app.QueryAsync(db => db.Occurrences.Find(o => o.ReservationId == reservationId).Project(o => o.Id).SingleAsync());
        await WindBackAsync(app, reservationId, minutesAgo: 45);
        await app.QueryAsync(async db =>
        {
            await db.ParkingSessions.InsertOneAsync(new ParkingSession
            {
                OccurrenceId = occurrenceId, SpotId = spots[0].Id, LotId = lot.Id, PlateNo = "CP-05",
                EnteredAtUtc = DateTime.UtcNow.AddMinutes(-40), Status = SessionStatus.InProgress,
            });
            return true;
        });

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        Assert.Equal(0, await GuardService.SweepNoShowsAsync(db, scope.ServiceProvider.GetRequiredService<JobQueue>(),
            scope.ServiceProvider.GetRequiredService<RealtimeEvents>(), CancellationToken.None));

        Assert.Equal(OccurrenceStatus.Scheduled, await app.QueryAsync(db => db.Occurrences.Find(o => o.Id == occurrenceId).Project(o => o.Status).SingleAsync()));
        Assert.Equal(SpotState.Booked, await app.QueryAsync(db => db.Spots.Find(s => s.Id == spots[0].Id).Project(s => s.State).SingleAsync()));
        Assert.Equal(0, await db.Alerts.CountDocumentsAsync(a => a.UserId == customerId && a.Kind == "gate"));
    }

    static Task WindBackAsync(AppFactory app, Guid reservationId, int minutesAgo) =>
        app.QueryAsync(async db =>
        {
            await db.Occurrences.UpdateOneAsync(o => o.ReservationId == reservationId,
                Builders<Occurrence>.Update.Set(o => o.StartAtUtc, DateTime.UtcNow.AddMinutes(-minutesAgo)));
            return true;
        });
}
