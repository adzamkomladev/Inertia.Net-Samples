using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// Warranties, device insurance and repairs: customer endpoints and the admin care desk.
public sealed class CareService(AppDb db, JobQueue jobs, RealtimeEvents realtime)
{
    const int DeskLimit = 100;

    // Repairs only move forward through this flow.
    static readonly string[] RepairFlow =
        [RepairStatuses.Received, RepairStatuses.Quoted, RepairStatuses.Repairing, RepairStatuses.Ready, RepairStatuses.PickedUp, RepairStatuses.Closed];

    // ---------------------------------------------------------------- warranties

    public Task<List<WarrantyListRow>> WarrantiesAsync(Guid uid, CancellationToken ct) =>
        db.Warranties.AsNoTracking()
            .Where(w => w.UserId == uid)
            .OrderByDescending(w => w.EndOn)
            .Select(w => new WarrantyListRow(
                w.Id, w.OrderItem.Name, w.OrderItem.Order.Number, w.SerialNumber, w.StartOn, w.EndOn, w.Status,
                db.WarrantyClaims.Where(c => c.WarrantyId == w.Id)
                    .Select(c => new ClaimRow(c.Id, c.Issue, c.Status, c.DecisionNote, c.CreatedAt)).ToList()))
            .ToListAsync(ct);

    public async Task<SaveResult> SetSerialAsync(Guid uid, Guid id, SerialInput input, CancellationToken ct)
    {
        var updated = await db.Warranties.Where(w => w.Id == id && w.UserId == uid)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.SerialNumber, input.SerialNumber == null ? null : input.SerialNumber.Trim())
                .SetProperty(w => w.UpdatedAt, DateTime.UtcNow), ct);
        return updated == 0 ? SaveResult.Missing : SaveResult.Success("Serial number saved.");
    }

    /// Claims need an active warranty that has not passed its end date.
    public async Task<SaveResult> ClaimWarrantyAsync(Guid uid, Guid id, WarrantyClaimInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Issue)) return SaveResult.Invalid("issue", "Describe the problem.");
        var warranty = await db.Warranties.AsNoTracking().Where(w => w.Id == id && w.UserId == uid)
            .Select(w => new { w.Status, w.EndOn }).FirstOrDefaultAsync(ct);
        if (warranty is null) return SaveResult.Missing;
        if (warranty.Status != WarrantyStatuses.Active || DateOnly.FromDateTime(DateTime.UtcNow) > warranty.EndOn)
            return SaveResult.Error("Claims are only possible while the warranty is active.");
        db.WarrantyClaims.Add(new WarrantyClaim { WarrantyId = id, Issue = input.Issue.Trim() });
        db.Alerts.Add(new Alert
        {
            UserId = uid, Kind = AlertKinds.Warranty, Title = "Warranty claim submitted",
            Body = "Our team reviews warranty claims within 48 hours.", Href = "/account/care/warranties",
        });
        await db.SaveChangesAsync(ct);
        await realtime.NotifyUserAsync(uid, "Warranty claim submitted", "We will review it within 48 hours.", "/account/care/warranties", ct);
        return SaveResult.Success("Claim submitted.");
    }

    // ---------------------------------------------------------------- insurance

    public async Task<InsuranceData> InsuranceAsync(Guid uid, CancellationToken ct)
    {
        var policies = await db.DeviceInsurances.AsNoTracking().IgnoreQueryFilters()
            .Where(d => d.UserId == uid)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new PolicyRow(
                d.Id, d.DeviceName, d.DeviceSerial, d.Premium, d.CoverageAmount, d.StartOn, d.EndOn, d.Status, d.Product.Slug,
                db.InsuranceClaims.Where(c => c.DeviceInsuranceId == d.Id)
                    .Select(c => new InsuranceClaimRow(c.Id, c.IncidentType, c.Description, c.IncidentOn, c.PayoutAmount, c.Status, c.PaidAt, c.CreatedAt)).ToList()))
            .ToListAsync(ct);
        // Attachable: delivered items of insurable products that have no policy yet.
        var attachable = await db.OrderItems.AsNoTracking()
            .Where(i => i.Order.UserId == uid && i.Order.Status == OrderStatuses.Delivered && i.Product != null)
            .Select(i => new AttachableItem(i.Id, i.Name, i.Product!.Slug, db.DeviceInsurances.Any(d => d.UserId == uid && d.ProductId == i.ProductId)))
            .ToListAsync(ct);
        return new InsuranceData(policies, attachable);
    }

    public async Task<SaveResult> ClaimInsuranceAsync(Guid uid, Guid id, InsuranceClaimInput input, CancellationToken ct)
    {
        if (input.IncidentType is not (InsuranceIncidentTypes.Damage or InsuranceIncidentTypes.Theft or InsuranceIncidentTypes.Malfunction))
            return SaveResult.Invalid("incidentType", "Pick damage, theft or malfunction.");
        if (string.IsNullOrWhiteSpace(input.Description)) return SaveResult.Invalid("description", "Describe what happened.");
        var status = await db.DeviceInsurances.Where(d => d.Id == id && d.UserId == uid).Select(d => d.Status).FirstOrDefaultAsync(ct);
        if (status is null) return SaveResult.Missing;
        if (status != InsuranceStatuses.Active) return SaveResult.Error("Claims require an active policy.");
        db.InsuranceClaims.Add(new InsuranceClaim
        {
            DeviceInsuranceId = id,
            IncidentType = input.IncidentType,
            Description = input.Description.Trim(),
            IncidentOn = input.IncidentOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
        });
        db.Alerts.Add(new Alert
        {
            UserId = uid, Kind = AlertKinds.Insurance, Title = "Insurance claim submitted",
            Body = "We will review your claim and respond by email.", Href = "/account/care/insurance",
        });
        await db.SaveChangesAsync(ct);
        await realtime.NotifyUserAsync(uid, "Insurance claim submitted", "We will review your claim and respond by email.", "/account/care/insurance", ct);
        return SaveResult.Success("Claim submitted.");
    }

    /// Attaches a policy to a delivered order item the customer owns.
    public async Task<SaveResult> AttachInsuranceAsync(Guid uid, Guid orderItemId, CancellationToken ct)
    {
        var item = await db.OrderItems.AsNoTracking()
            .Where(i => i.Id == orderItemId && i.Order.UserId == uid && i.Order.Status == OrderStatuses.Delivered && i.Product != null)
            .Select(i => new
            {
                i.Name, i.ProductId, i.OrderId, i.Product!.IsInsurable, i.Product.Price,
                Insured = db.DeviceInsurances.Any(d => d.UserId == uid && d.ProductId == i.ProductId || d.OrderId == i.OrderId),
            })
            .FirstOrDefaultAsync(ct);
        if (item is null) return SaveResult.Missing;
        if (!item.IsInsurable) return SaveResult.Error("That item does not offer device insurance.");
        if (item.Insured) return SaveResult.Info("That item already has a policy.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.DeviceInsurances.Add(new DeviceInsurance
        {
            UserId = uid,
            ProductId = item.ProductId,
            OrderId = item.OrderId,
            DeviceName = item.Name,
            Premium = Math.Round(item.Price * Pricing.InsurancePremiumPercent, 2, MidpointRounding.AwayFromZero),
            CoverageAmount = Math.Round(item.Price * Pricing.InsuranceCoveragePercent, 2, MidpointRounding.AwayFromZero),
            StartOn = today,
            EndOn = today.AddYears(1),
        });
        await db.SaveChangesAsync(ct);
        return SaveResult.Success("Policy attached.");
    }

    // ---------------------------------------------------------------- repairs

    public async Task<RepairsData> RepairsAsync(Guid uid, CancellationToken ct)
    {
        var repairs = await db.RepairTickets.AsNoTracking()
            .Where(r => r.UserId == uid)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new RepairListRow(r.Id, r.DeviceName, r.Problem, r.Status, r.QuotedCost, r.TechnicianName, r.AppointmentOn, r.CreatedAt, r.UpdatedAt))
            .ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder)
            .Select(c => new CategoryOption(c.Id, c.Name)).ToListAsync(ct);
        return new RepairsData(repairs, categories);
    }

    public async Task<RepairDetailData?> RepairDetailAsync(Guid uid, Guid id, CancellationToken ct)
    {
        var ticket = await db.RepairTickets.AsNoTracking()
            .Where(r => r.Id == id && r.UserId == uid)
            .Select(r => new RepairDetail(r.Id, r.DeviceName, r.DeviceSerial, r.Problem, r.Status, r.QuotedCost, r.TechnicianName,
                r.AppointmentOn, r.CreatedAt, r.UpdatedAt, r.StatusHistoryJson))
            .FirstOrDefaultAsync(ct);
        return ticket is null ? null : new RepairDetailData(ticket, RepairHistory.Read(ticket.StatusHistoryJson));
    }

    public async Task<SaveResult> CreateRepairAsync(Guid uid, RepairInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.DeviceName) || string.IsNullOrWhiteSpace(input.Problem))
            return SaveResult.Invalid(string.IsNullOrWhiteSpace(input.DeviceName) ? "deviceName" : "problem", "Tell us what device broke and what happened.");
        db.RepairTickets.Add(new RepairTicket
        {
            UserId = uid,
            DeviceName = input.DeviceName.Trim(),
            DeviceSerial = input.DeviceSerial,
            Problem = input.Problem.Trim(),
            CategoryId = Guid.TryParse(input.CategoryId, out var categoryId) ? categoryId : null,
            StatusHistoryJson = RepairHistory.Write([new RepairEvent(RepairStatuses.Received, "Ticket created", DateTime.UtcNow)]),
        });
        db.Alerts.Add(new Alert
        {
            UserId = uid, Kind = AlertKinds.Repair, Title = "Repair ticket received",
            Body = "We will inspect it and send a quote.", Href = "/account/care/repairs",
        });
        await db.SaveChangesAsync(ct);
        await realtime.NotifyUserAsync(uid, "Repair ticket received", "We will inspect it and send a quote.", "/account/care/repairs", ct);
        return SaveResult.Success("Repair ticket created.");
    }

    // ---------------------------------------------------------------- admin desk

    public Task<List<WarrantyClaimAdminRow>> WarrantyClaimsAsync(CancellationToken ct) =>
        db.WarrantyClaims.AsNoTracking().OrderByDescending(c => c.CreatedAt).Take(DeskLimit)
            .Select(c => new WarrantyClaimAdminRow(c.Id, c.Issue, c.Status, c.DecisionNote, c.CreatedAt, c.UpdatedAt,
                c.Warranty.OrderItem.Name, c.Warranty.OrderItem.Order.Number, c.Warranty.SerialNumber, c.Warranty.User.Name))
            .ToListAsync(ct);

    public Task<List<InsuranceClaimAdminRow>> InsuranceClaimsAsync(CancellationToken ct) =>
        db.InsuranceClaims.AsNoTracking().OrderByDescending(c => c.CreatedAt).Take(DeskLimit)
            .Select(c => new InsuranceClaimAdminRow(c.Id, c.IncidentType, c.Description, c.IncidentOn, c.PayoutAmount, c.Status, c.PaidAt,
                c.CreatedAt, c.Policy.DeviceName, c.Policy.CoverageAmount, c.Policy.User.Name))
            .ToListAsync(ct);

    public Task<List<RepairAdminRow>> RepairQueueAsync(CancellationToken ct) =>
        db.RepairTickets.AsNoTracking().OrderByDescending(t => t.CreatedAt).Take(DeskLimit)
            .Select(t => new RepairAdminRow(t.Id, t.DeviceName, t.DeviceSerial, t.Problem, t.Status, t.QuotedCost, t.TechnicianName,
                t.AppointmentOn, t.CreatedAt, t.User.Name, t.User.Email))
            .ToListAsync(ct);

    public async Task<SaveResult> DecideInsuranceClaimAsync(Guid id, InsuranceDecisionInput input, CancellationToken ct)
    {
        var claim = await db.InsuranceClaims.Include(c => c.Policy).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null) return SaveResult.Missing;
        var allowed = input.Decision switch
        {
            "review" => claim.Status == InsuranceClaimStatuses.Submitted,
            "approve" => claim.Status is InsuranceClaimStatuses.Submitted or InsuranceClaimStatuses.UnderReview,
            "pay" => claim.Status == InsuranceClaimStatuses.Approved,
            "reject" => claim.Status != InsuranceClaimStatuses.Paid,
            _ => (bool?)null,
        };
        if (allowed is null) return SaveResult.Error("Unknown decision.");
        if (allowed is false) return SaveResult.Error("That move is not allowed for this claim.");

        var now = DateTime.UtcNow;
        switch (input.Decision)
        {
            case "review": claim.Status = InsuranceClaimStatuses.UnderReview; break;
            case "approve": claim.Status = InsuranceClaimStatuses.Approved; break;
            case "pay":
                claim.Status = InsuranceClaimStatuses.Paid;
                claim.PayoutAmount = input.Payout is > 0 ? input.Payout : claim.PayoutAmount ?? claim.Policy.CoverageAmount;
                claim.PaidAt = now;
                break;
            default: claim.Status = InsuranceClaimStatuses.Rejected; break;
        }
        claim.UpdatedAt = now;
        var userId = claim.Policy.UserId;
        db.Alerts.Add(new Alert
        {
            UserId = userId, Kind = AlertKinds.Insurance, Title = $"Claim is now {claim.Status}",
            Body = input.Note ?? $"Decision: {claim.Status}.", Href = "/account/care/insurance",
        });
        jobs.Enqueue(db, new CareDecisionEmail("insurance-claim", id));
        await db.SaveChangesAsync(ct);
        jobs.Wake();
        await realtime.NotifyUserAsync(userId, "Insurance claim update", $"Your claim is now {claim.Status}.{(input.Note is null ? "" : " " + input.Note)}", "/account/care/insurance", ct);
        return SaveResult.Success($"Insurance claim {claim.Status}.");
    }

    public async Task<SaveResult> DecideWarrantyClaimAsync(Guid id, WarrantyDecisionInput input, CancellationToken ct)
    {
        var claim = await db.WarrantyClaims.Include(c => c.Warranty).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null) return SaveResult.Missing;
        var allowed = input.Decision switch
        {
            "review" => claim.Status == WarrantyClaimStatuses.Submitted,
            "approve" => claim.Status is WarrantyClaimStatuses.Submitted or WarrantyClaimStatuses.InReview,
            "ship" => claim.Status == WarrantyClaimStatuses.Approved,
            "resolve" => claim.Status is WarrantyClaimStatuses.Approved or WarrantyClaimStatuses.ReplacementShipped,
            "reject" => claim.Status != WarrantyClaimStatuses.Resolved,
            _ => (bool?)null,
        };
        if (allowed is null) return SaveResult.Error("Unknown decision.");
        if (allowed is false) return SaveResult.Error("That move is not allowed for this claim.");

        claim.Status = input.Decision switch
        {
            "review" => WarrantyClaimStatuses.InReview,
            "approve" => WarrantyClaimStatuses.Approved,
            "ship" => WarrantyClaimStatuses.ReplacementShipped,
            "resolve" => WarrantyClaimStatuses.Resolved,
            _ => WarrantyClaimStatuses.Rejected,
        };
        claim.DecisionNote = input.Note ?? claim.DecisionNote;
        claim.UpdatedAt = DateTime.UtcNow;
        var userId = claim.Warranty.UserId;
        db.Alerts.Add(new Alert
        {
            UserId = userId, Kind = AlertKinds.Warranty, Title = $"Warranty claim is now {claim.Status}",
            Body = input.Note ?? $"Decision: {claim.Status}.", Href = "/account/care/warranties",
        });
        jobs.Enqueue(db, new CareDecisionEmail("warranty-claim", id));
        await db.SaveChangesAsync(ct);
        jobs.Wake();
        await realtime.NotifyUserAsync(userId, "Warranty claim update", $"Your claim is now {claim.Status}.{(input.Note is null ? "" : " " + input.Note)}", "/account/care/warranties", ct);
        return SaveResult.Success($"Warranty claim {claim.Status}.");
    }

    public async Task<SaveResult> UpdateRepairAsync(Guid id, RepairUpdateInput input, CancellationToken ct)
    {
        var ticket = await db.RepairTickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return SaveResult.Missing;
        var changed = false;
        if (input.Status is not null && input.Status != ticket.Status)
        {
            var to = Array.IndexOf(RepairFlow, input.Status);
            if (to < 0) return SaveResult.Error("Unknown repair status.");
            if (Array.IndexOf(RepairFlow, ticket.Status) >= to)
                return SaveResult.Error($"Repairs never go from {ticket.Status} to {input.Status}.");
            ticket.Status = input.Status;
            ticket.StatusHistoryJson = RepairHistory.Append(ticket.StatusHistoryJson,
                new RepairEvent(input.Status, input.Note ?? $"Status set to {input.Status}", DateTime.UtcNow));
            changed = true;
        }
        if (input.QuotedCost is > 0) { ticket.QuotedCost = input.QuotedCost; changed = true; }
        if (!string.IsNullOrWhiteSpace(input.TechnicianName)) { ticket.TechnicianName = input.TechnicianName.Trim(); changed = true; }
        if (input.AppointmentOn is { } when) { ticket.AppointmentOn = when; changed = true; }
        if (!changed) return SaveResult.Info("Nothing to update.");
        ticket.UpdatedAt = DateTime.UtcNow;
        jobs.Enqueue(db, new CareDecisionEmail("repair", id));
        await db.SaveChangesAsync(ct);
        jobs.Wake();
        await realtime.NotifyUserAsync(ticket.UserId, "Repair update", $"{ticket.DeviceName}: now {ticket.Status}.", "/account/care/repairs", ct);
        return SaveResult.Success($"Repair updated to {ticket.Status}.");
    }
}
