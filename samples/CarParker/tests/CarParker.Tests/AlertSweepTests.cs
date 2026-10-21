using MongoDB.Driver;
using CarParker.Features.Bookings;
using Microsoft.Extensions.DependencyInjection;
using CarParker.Data;
using CarParker.Features.Guard;
using CarParker.Infrastructure;

namespace CarParker.Tests;

/// Cron sweeps (overstay-check, reservation-sweeper) write their durable trail through alerts and jobs.
public sealed class AlertSweepTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Overstay_flagger_writes_one_deduped_alert_per_open_session()
    {
        var (customer, customerId) = await app.SharedUserAsync();
        var (lot, spots) = await app.SeedLotAsync("Overstay flag deck", spotCount: 1);
        using var booking = await app.BookAsync(customer, lot, spots[0], DateTime.UtcNow.AddMinutes(30), DateTime.UtcNow.AddMinutes(90));
        var reservationId = AppFactory.BookingIdOf(booking);

        // An open session whose window ended more than the 20 min overstay grace ago.
        var occurrenceId = await app.QueryAsync(db => db.Occurrences.Find(o => o.ReservationId == reservationId).Project(o => o.Id).SingleAsync());
        var sessionId = Guid.CreateVersion7();
        await app.QueryAsync(async db =>
        {
            await db.Occurrences.UpdateOneAsync(o => o.Id == occurrenceId,
                Builders<Occurrence>.Update
                    .Set(o => o.StartAtUtc, DateTime.UtcNow.AddHours(-2))
                    .Set(o => o.EndAtUtc, DateTime.UtcNow.AddHours(-1)));
            await db.ParkingSessions.InsertOneAsync(new ParkingSession
            {
                Id = sessionId, OccurrenceId = occurrenceId, SpotId = spots[0].Id, LotId = lot.Id,
                PlateNo = "CP-77", EnteredAtUtc = DateTime.UtcNow.AddHours(-3), Status = SessionStatus.InProgress,
            });
            return true;
        });

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var realtime = scope.ServiceProvider.GetRequiredService<RealtimeEvents>();
        Assert.Equal(1, await GuardService.FlagOverstaysAsync(db, realtime, CancellationToken.None));
        Assert.Equal(1, await db.Alerts.CountDocumentsAsync(a => a.UserId == customerId && a.Kind == "overstay" && a.Body == $"session:{sessionId}"));

        // The second sweep is deduplicated on the session marker.
        Assert.Equal(0, await GuardService.FlagOverstaysAsync(db, realtime, CancellationToken.None));
        Assert.Equal(1, await db.Alerts.CountDocumentsAsync(a => a.UserId == customerId && a.Kind == "overstay" && a.Body == $"session:{sessionId}"));
    }

    [Fact]
    public async Task Reminder_jobs_leave_the_sweep_marker_alert()
    {
        var (customer, customerId) = await app.SharedUserAsync();
        var (lot, spots) = await app.SeedLotAsync("Reminder deck", spotCount: 1);
        var start = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(1).AddHours(8), DateTimeKind.Utc);
        using var booking = await app.BookAsync(customer, lot, spots[0], start, start.AddHours(2));
        var reservationId = AppFactory.BookingIdOf(booking);
        var occurrenceId = await app.QueryAsync(db => db.Occurrences.Find(o => o.ReservationId == reservationId).Project(o => o.Id).SingleAsync());

        // What the reservation-sweeper cron enqueues for tomorrow's occurrences.
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await scope.ServiceProvider.GetRequiredService<JobQueue>().EnqueueAsync(db, new ReservationReminderEmail(reservationId, occurrenceId));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        JobStatus? status = null;
        while (DateTime.UtcNow < deadline)
        {
            status = await app.QueryAsync(async db2 => (await db2.Jobs.Find(j => j.Type == "ReservationReminderEmail" && j.Payload.Contains(occurrenceId.ToString())).FirstOrDefaultAsync())?.Status);
            if (status == JobStatus.Done) break;
            await Task.Delay(100);
        }
        Assert.Equal(JobStatus.Done, status);
        Assert.Equal(1, await app.QueryAsync(db2 => db2.Alerts.CountDocumentsAsync(a => a.UserId == customerId && a.Kind == "reservation_soon" && a.Body == $"occurrence:{occurrenceId}")));
    }
}
