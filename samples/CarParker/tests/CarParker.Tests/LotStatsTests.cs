using MongoDB.Driver;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
using Microsoft.Extensions.DependencyInjection;
using CarParker.Data;
using CarParker.Features.Analytics;
using CarParker.Features.Lots;

namespace CarParker.Tests;

public sealed class LotStatsTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Rollup_writes_daily_rows_from_sessions_and_paid_payments()
    {
        var (lot, spots) = await app.SeedLotAsync("Stats deck", spotCount: 2);
        var now = DateTime.UtcNow;
        await app.QueryAsync(async db =>
        {
            var reservationId = Guid.CreateVersion7();
            await db.Reservations.InsertOneAsync(new Reservation
            {
                Id = reservationId, UserId = Guid.CreateVersion7(), LotId = lot.Id, SpotId = spots[0].Id,
                WindowType = WindowType.Single, StartAtUtc = now.AddHours(-3), EndAtUtc = now.AddHours(-1),
                ParkingCents = 1500, TotalCents = 1500,
            });
            await db.Payments.InsertOneAsync(new Payment
            {
                ReservationId = reservationId, UserId = Guid.CreateVersion7(), Method = PaymentMethod.Card,
                AmountCents = 1500, Status = PaymentStatus.Paid, PaidAt = now,
            });
            await db.ParkingSessions.InsertOneAsync(new ParkingSession
            {
                OccurrenceId = Guid.Empty, SpotId = spots[0].Id, LotId = lot.Id, PlateNo = "CP-11",
                EnteredAtUtc = now.AddHours(-4), ExitedAtUtc = now.AddHours(-3), Status = SessionStatus.Exited,
            });
            return true;
        });

        await using var scope = app.Services.CreateAsyncScope();
        var analytics = scope.ServiceProvider.GetRequiredService<LotAnalyticsService>();
        await analytics.RollupAsync(CancellationToken.None);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var stat = await app.QueryAsync(db2 => db2.LotDailyStats.Find(s => s.LotId == lot.Id && s.Date == today).SingleAsync());
        Assert.Equal(1, stat.Sessions);
        Assert.Equal(1500, stat.RevenueCents);
        Assert.Equal(1, stat.Reservations);
        Assert.True(stat.OccupancyPct > 0); // one spot-hour across two spots over 24h

        var kpis = await analytics.KpisAsync(Guid.Empty, 30, CancellationToken.None);
        Assert.Equal(1500, kpis.RevenueCents);
        Assert.Equal(1, kpis.Sessions);
        Assert.Equal(1, kpis.Reservations);
        Assert.Equal(0, kpis.NoShowPct);
    }

    [Fact]
    public async Task Idle_lots_get_zero_filled_daily_rows()
    {
        var (lot, _) = await app.SeedLotAsync("Idle deck", spotCount: 1);
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LotAnalyticsService>().RollupAsync(CancellationToken.None);

        var stat = await app.QueryAsync(db => db.LotDailyStats.Find(s => s.LotId == lot.Id && s.Date == DateOnly.FromDateTime(DateTime.UtcNow)).SingleAsync());
        Assert.Equal(0, stat.Sessions);
        Assert.Equal(0, stat.RevenueCents);
        Assert.Equal(0, stat.Reservations);
        Assert.Equal(0, stat.OccupancyPct);
    }
}
