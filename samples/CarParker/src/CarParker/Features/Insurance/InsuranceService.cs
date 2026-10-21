using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;

namespace CarParker.Features.Insurance;

/// <summary>Spot Cover lifecycle: claim intake shared by the customer console and decision endpoints for staff.</summary>
public static class InsuranceService
{
    /// <summary>Creates a submitted claim against an active cover. The caller validated ownership and the amount cap.</summary>
    public static async Task<PolicyClaim> CreateClaimAsync(AppDb db, SpotCoverPolicy policy, ClaimKind kind,
        string description, DateTime incidentOn, long claimedCents, CancellationToken ct = default)
    {
        var claim = new PolicyClaim
        {
            PolicyId = policy.Id,
            UserId = policy.UserId,
            Kind = kind,
            Description = description,
            IncidentOn = incidentOn,
            ClaimedCents = claimedCents,
            Status = ClaimStatus.Submitted,
        };
        await db.PolicyClaims.InsertOneAsync(claim, cancellationToken: ct);
        return claim;
    }

    /// <summary>Approve: fixes the payout (≤ coverage), sets reviewer; returns false when the claim was already decided.</summary>
    public static async Task<bool> ApproveAsync(AppDb db, Guid claimId, long payoutCents, Guid adminId, string? note, CancellationToken ct = default)
    {
        var claim = await db.PolicyClaims.Find(c => c.Id == claimId).FirstOrDefaultAsync(ct);
        if (claim is null || claim.Status is ClaimStatus.Approved or ClaimStatus.Paid or ClaimStatus.Rejected) return false;
        var policy = await db.SpotCoverPolicies.Find(p => p.Id == claim.PolicyId).FirstOrDefaultAsync(ct);
        if (policy is null) return false;
        var payout = Math.Clamp(payoutCents, 0, policy.CoverageCents);
        var result = await db.PolicyClaims.UpdateOneAsync(
            c => c.Id == claimId && c.Status == ClaimStatus.Submitted,
            Builders<PolicyClaim>.Update
                .Set(c => c.Status, ClaimStatus.Approved)
                .Set(c => c.PayoutCents, payout)
                .Set(c => c.DecisionNote, note)
                .Set(c => c.DecidedByUserId, adminId)
                .Set(c => c.DecidedAt, DateTime.UtcNow),
            cancellationToken: ct);
        return result.ModifiedCount == 1;
    }

    /// <summary>Pay an approved claim and notify the customer by email (when they opted in). Idempotent on status.</summary>
    public static async Task<bool> PayClaimAsync(AppDb db, JobQueue jobs, Guid claimId, Guid adminId, CancellationToken ct = default)
    {
        var result = await db.PolicyClaims.UpdateOneAsync(
            c => c.Id == claimId && c.Status == ClaimStatus.Approved,
            Builders<PolicyClaim>.Update
                .Set(c => c.Status, ClaimStatus.Paid)
                .Set(c => c.DecidedByUserId, adminId)
                .Set(c => c.PaidAt, DateTime.UtcNow),
            cancellationToken: ct);
        return result.ModifiedCount == 1;
    }

    public static async Task<bool> RejectAsync(AppDb db, Guid claimId, Guid adminId, string? note, CancellationToken ct = default)
    {
        var result = await db.PolicyClaims.UpdateOneAsync(
            c => c.Id == claimId && (c.Status == ClaimStatus.Submitted || c.Status == ClaimStatus.UnderReview),
            Builders<PolicyClaim>.Update
                .Set(c => c.Status, ClaimStatus.Rejected)
                .Set(c => c.DecisionNote, note)
                .Set(c => c.DecidedByUserId, adminId)
                .Set(c => c.DecidedAt, DateTime.UtcNow),
            cancellationToken: ct);
        return result.ModifiedCount == 1;
    }

    /// <summary>Cron: expire active covers whose latest reservation occurrence has ended.</summary>
    public static async Task<int> ExpireOverdueAsync(AppDb db, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var active = await db.SpotCoverPolicies.Find(p => p.Status == PolicyStatus.Active).ToListAsync(ct);
        if (active.Count == 0) return 0;
        var reservationIds = active.Select(p => p.ReservationId).ToArray();
        var ends = await db.Occurrences.Find(o => reservationIds.Contains(o.ReservationId))
            .Project(o => new { o.ReservationId, o.EndAtUtc }).ToListAsync(ct);
        var endByReservation = ends.GroupBy(x => x.ReservationId).ToDictionary(g => g.Key, g => g.Max(x => x.EndAtUtc));
        var expired = 0;
        foreach (var policy in active)
        {
            if (endByReservation.TryGetValue(policy.ReservationId, out var end) && end > now) continue;
            var result = await db.SpotCoverPolicies.UpdateOneAsync(
                p => p.Id == policy.Id && p.Status == PolicyStatus.Active,
                Builders<SpotCoverPolicy>.Update.Set(p => p.Status, PolicyStatus.Expired), cancellationToken: ct);
            if (result.ModifiedCount == 1) expired++;
        }
        return expired;
    }
}
