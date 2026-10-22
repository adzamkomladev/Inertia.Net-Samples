using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Bookings;
using CarParker.Infrastructure;

namespace CarParker.Features.Insurance;

public enum PolicyStatus { Active, Expired, Void }
public enum ClaimKind { Damage, Theft, BrokenWindow, Other }
public enum ClaimStatus { Submitted, UnderReview, Approved, Paid, Rejected }

/// <summary>"Spot Cover": one policy per reservation, priced at booking.</summary>
public sealed class SpotCoverPolicy
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ReservationId { get; set; }
    public Guid UserId { get; set; }
    public Guid LotId { get; set; }
    public long PremiumCents { get; set; }
    public long CoverageCents { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    [BsonIgnoreIfNull] public DateTime? EndedAt { get; set; }
    [BsonRepresentation(BsonType.String)] public PolicyStatus Status { get; set; } = PolicyStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class PolicyClaim
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PolicyId { get; set; }
    public Guid UserId { get; set; }
    [BsonRepresentation(BsonType.String)] public ClaimKind Kind { get; set; } = ClaimKind.Other;
    public required string Description { get; set; }
    public DateTime IncidentOn { get; set; }
    public long ClaimedCents { get; set; }
    [BsonIgnoreIfNull] public long? PayoutCents { get; set; }
    [BsonRepresentation(BsonType.String)] public ClaimStatus Status { get; set; } = ClaimStatus.Submitted;
    [BsonIgnoreIfNull] public string? DecisionNote { get; set; }
    [BsonIgnoreIfNull] public Guid? DecidedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonIgnoreIfNull] public DateTime? DecidedAt { get; set; }
    [BsonIgnoreIfNull] public DateTime? PaidAt { get; set; }
}

public sealed record CustomerPolicyRow(
    Guid Id, long PremiumCents, long CoverageCents, string Status, DateTime StartedAt, DateTime CreatedAt,
    Guid ReservationId, string? LotName, DateTime? ReservedThrough);

public sealed record CustomerClaimRow(
    Guid Id, Guid PolicyId, string Kind, string Description, long ClaimedCents, long? PayoutCents,
    string Status, DateTime CreatedAt, DateTime? PaidAt, string? DecisionNote);

public sealed record CustomerInsurancePage(CustomerPolicyRow[] Policies, CustomerClaimRow[] Claims);

public sealed record AdminPolicyRow(
    Guid Id, Guid ReservationId, long PremiumCents, long CoverageCents, DateTime StartedAt, string Status,
    string? LotName, string? UserName, string? UserEmail);

public sealed record AdminClaimRow(
    Guid Id, Guid PolicyId, string Description, long ClaimedCents, long? PayoutCents, DateTime IncidentOn,
    string? DecisionNote, string Kind, string Status, string? UserName, DateTime CreatedAt);

public sealed record AdminInsurancePage(AdminPolicyRow[] Policies, AdminClaimRow[] Claims);

public sealed class InsuranceService(AppDb db, JobQueue jobs, RealtimeEvents realtime)
{
    public async Task<CustomerInsurancePage> CustomerPageAsync(Guid userId, CancellationToken ct)
    {
        var policies = await db.SpotCoverPolicies.Find(p => p.UserId == userId).SortByDescending(p => p.CreatedAt).ToListAsync(ct);
        var claims = await db.PolicyClaims.Find(c => c.UserId == userId).SortByDescending(c => c.CreatedAt).ToListAsync(ct);
        var reservationIds = policies.Select(p => p.ReservationId).ToArray();
        var lotIds = policies.Select(p => p.LotId).Distinct().ToArray();
        var ends = (await db.Reservations.Find(r => reservationIds.Contains(r.Id)).Project(r => new { r.Id, r.EndAtUtc }).ToListAsync(ct))
            .ToDictionary(r => r.Id, r => r.EndAtUtc);
        var lotNames = (await db.Lots.Find(l => lotIds.Contains(l.Id)).Project(l => new { l.Id, l.Name }).ToListAsync(ct))
            .ToDictionary(l => l.Id, l => l.Name);
        return new CustomerInsurancePage(
            policies.Select(p => new CustomerPolicyRow(
                p.Id, p.PremiumCents, p.CoverageCents, p.Status.ToString().ToLowerInvariant(), p.StartedAt, p.CreatedAt, p.ReservationId,
                lotNames.GetValueOrDefault(p.LotId), ends.GetValueOrDefault(p.ReservationId))).ToArray(),
            claims.Select(c => new CustomerClaimRow(
                c.Id, c.PolicyId, c.Kind.ToString().ToLowerInvariant(), c.Description, c.ClaimedCents, c.PayoutCents,
                c.Status.ToString().ToLowerInvariant(), c.CreatedAt, c.PaidAt, c.DecisionNote)).ToArray());
    }

    public async Task<AdminInsurancePage> AdminPageAsync(CancellationToken ct)
    {
        var policies = await db.SpotCoverPolicies.Find(_ => true).SortByDescending(p => p.CreatedAt).Limit(100).ToListAsync(ct);
        var claims = await db.PolicyClaims.Find(_ => true).SortByDescending(c => c.CreatedAt).Limit(100).ToListAsync(ct);
        var lotIds = policies.Select(p => p.LotId).Distinct().ToArray();
        var userIds = policies.Select(p => p.UserId).Concat(claims.Select(c => c.UserId)).Distinct().ToArray();
        var lotNames = (await db.Lots.Find(l => lotIds.Contains(l.Id)).Project(l => new { l.Id, l.Name }).ToListAsync(ct))
            .ToDictionary(l => l.Id, l => l.Name);
        var users = (await db.Users.Find(u => userIds.Contains(u.Id)).Project(u => new { u.Id, u.Name, u.Email }).ToListAsync(ct))
            .ToDictionary(u => u.Id);
        return new AdminInsurancePage(
            policies.Select(p => new AdminPolicyRow(
                p.Id, p.ReservationId, p.PremiumCents, p.CoverageCents, p.StartedAt, p.Status.ToString().ToLowerInvariant(),
                lotNames.GetValueOrDefault(p.LotId), users.GetValueOrDefault(p.UserId)?.Name, users.GetValueOrDefault(p.UserId)?.Email)).ToArray(),
            claims.Select(c => new AdminClaimRow(
                c.Id, c.PolicyId, c.Description, c.ClaimedCents, c.PayoutCents, c.IncidentOn, c.DecisionNote,
                c.Kind.ToString().ToLowerInvariant(), c.Status.ToString().ToLowerInvariant(), users.GetValueOrDefault(c.UserId)?.Name, c.CreatedAt)).ToArray());
    }

    public async Task<Outcome> CreateClaimAsync(Guid policyId, ClaimInput input, Guid userId, CancellationToken ct)
    {
        var policy = await db.SpotCoverPolicies.Find(p => p.Id == policyId).FirstOrDefaultAsync(ct);
        if (policy is null) return Outcome.Missing();
        if (policy.UserId != userId) return Outcome.Denied();
        if (!Enum.TryParse<ClaimKind>(input.Kind, ignoreCase: true, out var kind))
            return Outcome.Fail("kind", "Choose a claim type.");
        if (policy.Status != PolicyStatus.Active)
            return Outcome.FlashBack(new Toast("error", "This cover is not active."));
        if (input.ClaimedCents > policy.CoverageCents)
            return Outcome.Fail("claimedCents", $"Claims are capped at the policy coverage (${policy.CoverageCents / 100.0:F2}).");

        var claim = new PolicyClaim
        {
            PolicyId = policy.Id,
            UserId = policy.UserId,
            Kind = kind,
            Description = input.Description!.Trim(),
            IncidentOn = input.IncidentOn ?? DateTime.UtcNow,
            ClaimedCents = input.ClaimedCents,
        };
        await db.PolicyClaims.InsertOneAsync(claim, cancellationToken: ct);
        await realtime.NotifyStaffAsync($"New Spot Cover claim ({kind})", "/admin/insurance", ct);
        await realtime.BoardChangedAsync("claim", ct);
        return Outcome.FlashBack(new Toast("success", $"Claim {claim.Id.ToString()[..8]} submitted."));
    }

    public async Task<Outcome> ApproveClaimAsync(Guid claimId, ClaimDecisionInput input, Guid adminId, CancellationToken ct)
    {
        var claim = await db.PolicyClaims.Find(c => c.Id == claimId).FirstOrDefaultAsync(ct);
        var coverage = claim is null ? (long?)null
            : await db.SpotCoverPolicies.Find(p => p.Id == claim.PolicyId).Project(p => (long?)p.CoverageCents).FirstOrDefaultAsync(ct);
        var approved = coverage is { } cap && (await db.PolicyClaims.UpdateOneAsync(
            c => c.Id == claimId && c.Status == ClaimStatus.Submitted,
            Builders<PolicyClaim>.Update
                .Set(c => c.Status, ClaimStatus.Approved)
                .Set(c => c.PayoutCents, Math.Clamp(input.PayoutCents, 0, cap))
                .Set(c => c.DecisionNote, input.Note)
                .Set(c => c.DecidedByUserId, adminId)
                .Set(c => c.DecidedAt, DateTime.UtcNow),
            cancellationToken: ct)).ModifiedCount == 1;
        if (!approved) return Outcome.FlashBack(new Toast("info", "Only submitted claims can be approved."));
        await realtime.BoardChangedAsync("claim", ct);
        return Outcome.FlashBack(new Toast("success", "Claim approved. Pay it to notify the customer."));
    }

    public async Task<Outcome> PayClaimAsync(Guid claimId, Guid adminId, CancellationToken ct)
    {
        var paid = await db.PolicyClaims.UpdateOneAsync(
            c => c.Id == claimId && c.Status == ClaimStatus.Approved,
            Builders<PolicyClaim>.Update
                .Set(c => c.Status, ClaimStatus.Paid)
                .Set(c => c.DecidedByUserId, adminId)
                .Set(c => c.PaidAt, DateTime.UtcNow),
            cancellationToken: ct);
        if (paid.ModifiedCount == 0) return Outcome.FlashBack(new Toast("info", "Only approved claims can be paid."));
        await jobs.EnqueueAsync(db, new ClaimDecisionEmail(claimId, "paid"), ct: ct);
        await realtime.BoardChangedAsync("claim", ct);
        return Outcome.FlashBack(new Toast("success", "Claim paid. The customer got the decision email."));
    }

    public async Task<Outcome> RejectClaimAsync(Guid claimId, ClaimDecisionInput input, Guid adminId, CancellationToken ct)
    {
        var rejected = await db.PolicyClaims.UpdateOneAsync(
            c => c.Id == claimId && (c.Status == ClaimStatus.Submitted || c.Status == ClaimStatus.UnderReview),
            Builders<PolicyClaim>.Update
                .Set(c => c.Status, ClaimStatus.Rejected)
                .Set(c => c.DecisionNote, input.Note)
                .Set(c => c.DecidedByUserId, adminId)
                .Set(c => c.DecidedAt, DateTime.UtcNow),
            cancellationToken: ct);
        if (rejected.ModifiedCount == 0) return Outcome.FlashBack(new Toast("info", "That claim was already decided."));
        await jobs.EnqueueAsync(db, new ClaimDecisionEmail(claimId, "rejected"), ct: ct);
        await realtime.BoardChangedAsync("claim", ct);
        return Outcome.FlashBack(new Toast("success", "Claim rejected."));
    }

    /// <summary>Cron: expire active covers whose last occurrence has ended.</summary>
    public async Task<long> ExpireOverdueAsync(CancellationToken ct)
    {
        var active = await db.SpotCoverPolicies.Find(p => p.Status == PolicyStatus.Active)
            .Project(p => new { p.Id, p.ReservationId }).ToListAsync(ct);
        if (active.Count == 0) return 0;
        var reservationIds = active.Select(p => p.ReservationId).ToArray();
        var stillRunning = await db.Occurrences.Distinct(o => o.ReservationId, o => reservationIds.Contains(o.ReservationId) && o.EndAtUtc > DateTime.UtcNow).ToListAsync(ct);
        var expiredIds = active.Where(p => !stillRunning.Contains(p.ReservationId)).Select(p => p.Id).ToArray();
        var result = await db.SpotCoverPolicies.UpdateManyAsync(
            p => expiredIds.Contains(p.Id) && p.Status == PolicyStatus.Active,
            Builders<SpotCoverPolicy>.Update.Set(p => p.Status, PolicyStatus.Expired), cancellationToken: ct);
        return result.ModifiedCount;
    }
}
