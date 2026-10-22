using System.Net;
using MongoDB.Driver;
using CarParker.Features.Insurance;
using CarParker.Data;

namespace CarParker.Tests;

public sealed class InsuranceTests(AppFactory app) : IClassFixture<AppFactory>
{
    /// Books one spot with Spot Cover on and returns the ids of the reservation and its policy.
    async Task<(Guid ReservationId, Guid PolicyId, long ParkingCents)> CoveredBookingAsync()
    {
        var (client, _) = await app.SharedUserAsync();
        var (lot, spots) = await app.SeedLotAsync($"Covered deck {Guid.NewGuid():N}", ratePerHourCents: 400);
        using var booking = await app.BookAsync(client, lot, spots[0], DateTime.UtcNow.AddHours(2), DateTime.UtcNow.AddHours(4), insurance: true);
        var reservationId = AppFactory.BookingIdOf(booking);
        var (insuranceId, parkingCents) = await app.QueryAsync(async db =>
        {
            var r = await db.Reservations.Find(x => x.Id == reservationId).SingleAsync();
            return (r.InsuranceId!.Value, r.ParkingCents);
        });
        return (reservationId, insuranceId, parkingCents);
    }

    [Fact]
    public async Task Bookings_with_spot_cover_get_an_active_policy_with_the_minimum_premium()
    {
        var (client, userId) = await app.SharedUserAsync();
        var (lot, spots) = await app.SeedLotAsync("Min premium deck", ratePerHourCents: 400);
        using var booking = await app.BookAsync(client, lot, spots[0], DateTime.UtcNow.AddHours(2), DateTime.UtcNow.AddHours(4), insurance: true);
        var reservationId = AppFactory.BookingIdOf(booking);

        var reservation = await app.ReservationAsync(reservationId);
        Assert.NotNull(reservation);
        Assert.Equal(800, reservation!.ParkingCents);
        Assert.Equal(200, reservation.InsuranceCents); // 12% of $8 = 96c, bumped to the $2 minimum
        Assert.Equal(1000, reservation.TotalCents);
        Assert.NotNull(reservation.InsuranceId);

        var policy = await app.QueryAsync(async db => await db.SpotCoverPolicies.Find(p => p.Id == reservation.InsuranceId).SingleAsync());
        Assert.Equal(PolicyStatus.Active, policy.Status);
        Assert.Equal(200, policy.PremiumCents);
        Assert.Equal(lot.CoverageCents, policy.CoverageCents);
        Assert.Equal(userId, policy.UserId);
    }

    [Fact]
    public async Task Claims_run_submitted_to_approved_to_paid_with_a_decision_email()
    {
        var (admin, adminId) = await app.SharedUserAsync(UserRole.Admin);
        var (client, _) = await app.SharedUserAsync();
        var (reservationId, policyId, _) = await CoveredBookingAsync();

        using var submitted = await app.SendAsync(client, $"/account/insurance/{policyId}/claims", new { kind = "damage", description = "Dented door", claimedCents = 1000 });
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        var claim = await app.QueryAsync(async db => await db.PolicyClaims.Find(c => c.PolicyId == policyId).SingleAsync());
        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.Equal(1000, claim.ClaimedCents);
        Assert.Null(claim.PayoutCents);

        using var approved = await app.SendAsync(admin, $"/admin/claims/{claim.Id}/approve", new { payoutCents = 900, note = "assessor signed off" });
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        claim = await app.QueryAsync(async db => await db.PolicyClaims.Find(c => c.Id == claim.Id).SingleAsync());
        Assert.Equal(ClaimStatus.Approved, claim.Status);
        Assert.Equal(900, claim.PayoutCents);
        Assert.Equal("assessor signed off", claim.DecisionNote);
        Assert.Equal(adminId, claim.DecidedByUserId);

        using var paid = await app.SendAsync(admin, $"/admin/claims/{claim.Id}/pay", new { });
        Assert.Equal(HttpStatusCode.Redirect, paid.StatusCode);
        claim = await app.QueryAsync(async db => await db.PolicyClaims.Find(c => c.Id == claim.Id).SingleAsync());
        Assert.Equal(ClaimStatus.Paid, claim.Status);
        Assert.NotNull(claim.PaidAt);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        JobStatus? status = null;
        while (DateTime.UtcNow < deadline)
        {
            status = await app.QueryAsync(async db => (await db.Jobs.Find(j => j.Type == "ClaimDecisionEmail" && j.Payload.Contains(claim.Id.ToString())).FirstOrDefaultAsync())?.Status);
            if (status == JobStatus.Done) break;
            await Task.Delay(100);
        }
        Assert.Equal(JobStatus.Done, status);
    }

    [Fact]
    public async Task Claims_above_coverage_are_refused_at_intake_and_payouts_are_clamped()
    {
        var (admin, _) = await app.SharedUserAsync(UserRole.Admin);
        var (client, _) = await app.SharedUserAsync();
        var (_, policyId, _) = await CoveredBookingAsync();

        // Intake refuses claims over the coverage cap.
        using var refused = await app.SendAsync(client, $"/account/insurance/{policyId}/claims", new { kind = "theft", description = "Too much", claimedCents = 60_000 });
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        Assert.Equal(0, await app.QueryAsync(async db => await db.PolicyClaims.CountDocumentsAsync(c => c.PolicyId == policyId)));

        // Approval clamps the payout to the coverage cap.
        using var submitted = await app.SendAsync(client, $"/account/insurance/{policyId}/claims", new { kind = "theft", description = "Within range", claimedCents = 50_000 });
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        var claimId = await app.QueryAsync(async db => await db.PolicyClaims.Find(c => c.PolicyId == policyId).Project(c => c.Id).SingleAsync());
        using (await app.SendAsync(admin, $"/admin/claims/{claimId}/approve", new { payoutCents = 10_000_000 })) { }
        var claim = await app.QueryAsync(async db => await db.PolicyClaims.Find(c => c.Id == claimId).SingleAsync());
        Assert.Equal(ClaimStatus.Approved, claim.Status);
        Assert.Equal(50_000, claim.PayoutCents);
    }
}
