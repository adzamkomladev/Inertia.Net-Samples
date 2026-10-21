using System.Net;
using MongoDB.Driver;
using SvelteMongoBlog.Features.Bookings;
using SvelteMongoBlog.Features.Guard;
using SvelteMongoBlog.Features.Lots;
using Microsoft.Extensions.DependencyInjection;
using SvelteMongoBlog.Data;

namespace SvelteMongoBlog.Tests;

public sealed class SessionFlowTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Check_in_needs_a_window_inside_the_grace_and_settles_pay_at_gate()
    {
        var (customer, _) = await app.SharedUserAsync();
        var (guard, guardId) = await app.SharedUserAsync(UserRole.Guard);
        var (client, _) = await app.SharedUserAsync();
        var (lot, spots) = await app.SeedLotAsync("Grace deck", spotCount: 2);
        var lateStart = DateTime.UtcNow.AddHours(2);

        // Far outside the ±15 min grace: refused, nothing changes.
        using var far = await app.BookAsync(customer, lot, spots[0], lateStart, lateStart.AddHours(2));
        using var refused = await app.SendAsync(guard, "/guard/check-in", new { lotId = lot.Id, spotId = spots[0].Id, plateNo = "cp-01" });
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        Assert.Equal(0, await app.QueryAsync(db => db.ParkingSessions.CountDocumentsAsync(s => s.SpotId == spots[0].Id)));
        Assert.Equal(SpotState.Booked, await app.QueryAsync(db => db.Spots.Find(s => s.Id == spots[0].Id).Project(s => s.State).SingleAsync()));

        // Within grace on a pay-at-gate booking: the session starts and the gate payment settles.
        using var near = await app.BookAsync(customer, lot, spots[1], DateTime.UtcNow.AddMinutes(5), DateTime.UtcNow.AddMinutes(65), method: "pay_at_gate");
        var reservationId = AppFactory.BookingIdOf(near);
        using var checkedIn = await app.SendAsync(guard, "/guard/check-in", new { lotId = lot.Id, spotId = spots[1].Id, plateNo = "cp-01" });
        Assert.Equal(HttpStatusCode.Redirect, checkedIn.StatusCode);

        var session = await app.QueryAsync(db => db.ParkingSessions.Find(s => s.SpotId == spots[1].Id).SingleAsync());
        Assert.Equal(SessionStatus.InProgress, session.Status);
        Assert.Equal("CP-01", session.PlateNo);
        Assert.Equal(guardId, session.EnteredById);
        Assert.Equal(SpotState.Occupied, await app.QueryAsync(db => db.Spots.Find(s => s.Id == spots[1].Id).Project(s => s.State).SingleAsync()));
        var occurrence = await app.QueryAsync(db => db.Occurrences.Find(o => o.ReservationId == reservationId).SingleAsync());
        Assert.Equal(OccurrenceStatus.Active, occurrence.Status);
        Assert.Equal(occurrence.Id, session.OccurrenceId);
        var payment = await app.QueryAsync(db => db.Payments.Find(p => p.ReservationId == reservationId).SingleAsync());
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.StartsWith("gate-", payment.TxRef);
    }

    [Fact]
    public async Task Check_out_ends_the_session_completes_the_reservation_and_notifies_the_driver()
    {
        var (customer, customerId) = await app.SharedUserAsync();
        var (guard, guardId) = await app.SharedUserAsync(UserRole.Guard);
        var (lot, spots) = await app.SeedLotAsync("Checkout deck", spotCount: 1);
        using var booking = await app.BookAsync(customer, lot, spots[0], DateTime.UtcNow.AddMinutes(5), DateTime.UtcNow.AddMinutes(65));
        var reservationId = AppFactory.BookingIdOf(booking);
        using (await app.SendAsync(guard, "/guard/check-in", new { lotId = lot.Id, spotId = spots[0].Id })) { }

        using var checkedOut = await app.SendAsync(guard, "/guard/check-out", new { lotId = lot.Id, spotId = spots[0].Id });
        Assert.Equal(HttpStatusCode.Redirect, checkedOut.StatusCode);

        var session = await app.QueryAsync(db => db.ParkingSessions.Find(s => s.SpotId == spots[0].Id).SingleAsync());
        Assert.Equal(SessionStatus.Exited, session.Status);
        Assert.NotNull(session.ExitedAtUtc);
        Assert.Equal(guardId, session.ExitedById);
        Assert.Null(session.CancelReason); // a plain checkout is not a forced close
        Assert.Equal(SpotState.Free, await app.QueryAsync(db => db.Spots.Find(s => s.Id == spots[0].Id).Project(s => s.State).SingleAsync()));
        Assert.Equal(OccurrenceStatus.Done, await app.QueryAsync(db => db.Occurrences.Find(o => o.ReservationId == reservationId).Project(o => o.Status).SingleAsync()));
        var reservation = await app.ReservationAsync(reservationId);
        Assert.Equal(ReservationStatus.Completed, reservation!.Status);
        Assert.NotNull(reservation.CompletedAt);
        Assert.Equal(1, await app.QueryAsync(db => db.Alerts.CountDocumentsAsync(a => a.UserId == customerId && a.Kind == "session")));

        // The checkout email job is processed by the worker (empty Maileroo key logs instead of sending).
        var deadline = DateTime.UtcNow.AddSeconds(10);
        JobStatus? status = null;
        while (DateTime.UtcNow < deadline)
        {
            status = await app.QueryAsync(async db => (await db.Jobs.Find(j => j.Type == "SendEmail").FirstOrDefaultAsync())?.Status);
            if (status == JobStatus.Done) break;
            await Task.Delay(100);
        }
        Assert.Equal(JobStatus.Done, status);
    }

    [Fact]
    public async Task Guards_resolve_overstays_with_a_note_and_alert_the_driver()
    {
        var (customer, customerId) = await app.SharedUserAsync();
        var (guard, _) = await app.SharedUserAsync(UserRole.Guard);
        var (lot, spots) = await app.SeedLotAsync("Overstay deck", spotCount: 1);
        using var booking = await app.BookAsync(customer, lot, spots[0], DateTime.UtcNow.AddMinutes(5), DateTime.UtcNow.AddMinutes(65));
        using (await app.SendAsync(guard, "/guard/check-in", new { lotId = lot.Id, spotId = spots[0].Id })) { }
        var sessionId = await app.QueryAsync(db => db.ParkingSessions.Find(s => s.SpotId == spots[0].Id).Project(s => s.Id).SingleAsync());

        using var resolved = await app.SendAsync(guard, $"/guard/sessions/{sessionId}/resolve?note=valet", new { });
        Assert.Equal(HttpStatusCode.Redirect, resolved.StatusCode);

        var session = await app.QueryAsync(db => db.ParkingSessions.Find(s => s.Id == sessionId).SingleAsync());
        Assert.Equal(SessionStatus.Exited, session.Status);
        Assert.Equal("overstay: valet", session.CancelReason);
        Assert.Equal(SpotState.Free, await app.QueryAsync(db => db.Spots.Find(s => s.Id == spots[0].Id).Project(s => s.State).SingleAsync()));
        Assert.Equal(1, await app.QueryAsync(db => db.Alerts.CountDocumentsAsync(a => a.UserId == customerId && a.Kind == "overstay" && a.Title == "Overstay resolved")));
    }

    [Fact]
    public async Task Admins_can_force_close_an_open_session()
    {
        var (customer, _) = await app.SharedUserAsync();
        var (guard, _) = await app.SharedUserAsync(UserRole.Guard);
        var (admin, adminId) = await app.SharedUserAsync(UserRole.Admin);
        var (lot, spots) = await app.SeedLotAsync("Force close deck", spotCount: 1);
        using var booking = await app.BookAsync(customer, lot, spots[0], DateTime.UtcNow.AddMinutes(5), DateTime.UtcNow.AddMinutes(65));
        using (await app.SendAsync(guard, "/guard/check-in", new { lotId = lot.Id, spotId = spots[0].Id })) { }
        var sessionId = await app.QueryAsync(db => db.ParkingSessions.Find(s => s.SpotId == spots[0].Id).Project(s => s.Id).SingleAsync());

        using var closed = await app.SendAsync(admin, $"/admin/sessions/{sessionId}/force-close?note=inspection", new { });
        Assert.Equal(HttpStatusCode.Redirect, closed.StatusCode);

        var session = await app.QueryAsync(db => db.ParkingSessions.Find(s => s.Id == sessionId).SingleAsync());
        Assert.Equal(SessionStatus.Exited, session.Status);
        Assert.Equal("force_close: inspection", session.CancelReason);
        Assert.Equal(adminId, session.ExitedById);
        Assert.Equal(SpotState.Free, await app.QueryAsync(db => db.Spots.Find(s => s.Id == spots[0].Id).Project(s => s.State).SingleAsync()));
    }
}
