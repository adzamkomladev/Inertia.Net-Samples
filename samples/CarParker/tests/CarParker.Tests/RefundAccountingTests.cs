using System.Net;
using MongoDB.Driver;
using CarParker.Features.Bookings;
using CarParker.Data;

namespace CarParker.Tests;

public sealed class RefundAccountingTests(AppFactory app) : IClassFixture<AppFactory>
{
    /// Books, pays and cancels ≥24h ahead so a full refund request is staged; returns the refund id.
    async Task<(Guid ReservationId, Guid RefundId)> CancelledBookingAsync(int hoursOut)
    {
        var (client, _) = await app.SharedUserAsync();
        var (lot, spots) = await app.SeedLotAsync($"Refund deck {Guid.NewGuid():N}", spotCount: 1);
        var start = DateTime.UtcNow.AddHours(hoursOut);
        using var booking = await app.BookAsync(client, lot, spots[0], start, start.AddHours(2));
        var reservationId = AppFactory.BookingIdOf(booking);
        using (await app.PayBookingAsync(client, reservationId)) { }
        using (await app.CancelBookingAsync(client, reservationId)) { }
        var refundId = await app.QueryAsync(async db => await db.Refunds.Find(rf => rf.ReservationId == reservationId).Project(rf => rf.Id).SingleAsync());
        return (reservationId, refundId);
    }

    [Fact]
    public async Task Approve_then_pay_completes_the_refund_and_notifies_the_driver()
    {
        var (admin, adminId) = await app.SharedUserAsync(UserRole.Admin);
        var (reservationId, refundId) = await CancelledBookingAsync(hoursOut: 30);

        using var approved = await app.SendAsync(admin, $"/admin/refunds/{refundId}/approve", new { amountCents = 800, note = "receipts checked" });
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        var refund = await app.QueryAsync(async db => await db.Refunds.Find(rf => rf.Id == refundId).SingleAsync());
        Assert.Equal(RefundStatus.Approved, refund.Status);
        Assert.Equal(800, refund.AmountCents);
        Assert.Equal("receipts checked", refund.DecisionNote);
        Assert.Equal(adminId, refund.ApprovedByUserId);

        using var paid = await app.SendAsync(admin, $"/admin/refunds/{refundId}/pay", new { });
        Assert.Equal(HttpStatusCode.Redirect, paid.StatusCode);
        refund = await app.QueryAsync(async db => await db.Refunds.Find(rf => rf.Id == refundId).SingleAsync());
        Assert.Equal(RefundStatus.Paid, refund.Status);
        Assert.NotNull(refund.PaidAt);
        Assert.Equal(PaymentStatus.Refunded, await app.QueryAsync(async db => await db.Payments.Find(p => p.ReservationId == reservationId).Project(p => p.Status).SingleAsync()));

        // The paid-notification job is processed by the worker (empty Maileroo key logs instead of sending).
        var deadline = DateTime.UtcNow.AddSeconds(10);
        JobStatus? status = null;
        while (DateTime.UtcNow < deadline)
        {
            status = await app.QueryAsync(async db => (await db.Jobs.Find(j => j.Type == "RefundPaidEmail" && j.Payload.Contains(refundId.ToString())).FirstOrDefaultAsync())?.Status);
            if (status == JobStatus.Done) break;
            await Task.Delay(100);
        }
        Assert.Equal(JobStatus.Done, status);
    }

    [Fact]
    public async Task Double_approval_cannot_change_a_decided_refund()
    {
        var (admin, _) = await app.SharedUserAsync(UserRole.Admin);
        var (_, refundId) = await CancelledBookingAsync(hoursOut: 30);

        using (await app.SendAsync(admin, $"/admin/refunds/{refundId}/approve", new { amountCents = 800, note = "first decision" })) { }
        using var second = await app.SendAsync(admin, $"/admin/refunds/{refundId}/approve", new { amountCents = 1, note = "sneaky retry" });
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode); // back with an "already decided" toast

        var refund = await app.QueryAsync(async db => await db.Refunds.Find(rf => rf.Id == refundId).SingleAsync());
        Assert.Equal(RefundStatus.Approved, refund.Status);
        Assert.Equal(800, refund.AmountCents);
        Assert.Equal("first decision", refund.DecisionNote);
    }

    [Fact]
    public async Task Rejection_records_a_note_and_blocks_approval()
    {
        var (admin, _) = await app.SharedUserAsync(UserRole.Admin);
        var (_, refundId) = await CancelledBookingAsync(hoursOut: 10); // half refund ($4 of $8)

        using var rejected = await app.SendAsync(admin, $"/admin/refunds/{refundId}/reject", new { amountCents = 400, note = "no proof of payment" });
        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        var refund = await app.QueryAsync(async db => await db.Refunds.Find(rf => rf.Id == refundId).SingleAsync());
        Assert.Equal(RefundStatus.Rejected, refund.Status);
        Assert.Equal("no proof of payment", refund.DecisionNote);

        using var approval = await app.SendAsync(admin, $"/admin/refunds/{refundId}/approve", new { amountCents = 400 });
        Assert.Equal(HttpStatusCode.Redirect, approval.StatusCode);
        Assert.Equal(RefundStatus.Rejected, await app.QueryAsync(async db => await db.Refunds.Find(rf => rf.Id == refundId).Project(rf => rf.Status).SingleAsync()));
    }
}
