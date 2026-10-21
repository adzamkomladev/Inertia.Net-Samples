using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// Warranty + device insurance + repairs (customer endpoints and admin care desk).
/// Returns plain data + SaveResult outcomes; the Care.cs route handlers compose the Inertia responses.
public static partial class CareService
{
    public sealed record InsuranceData(object Policies, List<AttachableItem> Attachable);
    public sealed record RepairsData(object Repairs, object Categories);
    public sealed record RepairDetailData(object Repair, List<object> History);
    public sealed record AdminData(string Tab, List<object> WarrantyClaims, List<object> InsuranceClaims, List<object> Repairs);

    // ---------------------------------------------------------------- warranties

    internal static async Task<object> GetWarrantiesAsync(Guid uid, AppDb db, CancellationToken ct)
    {
        return await db.Warranties.AsNoTracking()
            .Where(w => w.UserId == uid)
            .OrderByDescending(w => w.EndOn)
            .Select(w => new
            {
                w.Id,
                ItemName = w.OrderItem.Name,
                OrderNumber = w.OrderItem.Order.Number,
                w.SerialNumber,
                w.StartOn,
                w.EndOn,
                w.Status,
                Claims = db.WarrantyClaims.Where(c => c.WarrantyId == w.Id)
                    .Select(c => new ClaimRow(c.Id, c.Issue, c.Status, c.DecisionNote, c.CreatedAt)).ToList(),
            })
            .ToListAsync(ct);
    }

    internal static async Task<SaveResult> SetSerialAsync(Guid uid, Guid id, string? serial, AppDb db, CancellationToken ct)
    {
        var warranty = await db.Warranties.FirstOrDefaultAsync(w => w.Id == id && w.UserId == uid, ct);
        if (warranty is null)
            return new SaveResult(NotFound: true);
        warranty.SerialNumber = serial?.Trim();
        warranty.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new SaveResult(FlashType: "success", FlashMessage: "Serial number saved.");
    }

    /// Claim gate: active warranty and still before EndOn (server-checked).
    internal static async Task<SaveResult> ClaimWarrantyAsync(Guid uid, Guid id, string? issue, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(issue))
            return new SaveResult(Errors: new Dictionary<string, string> { ["issue"] = "Describe the problem." });
        var warranty = await db.Warranties.FirstOrDefaultAsync(w => w.Id == id && w.UserId == uid, ct);
        if (warranty is null)
            return new SaveResult(NotFound: true);
        if (warranty.Status != WarrantyStatuses.Active || DateOnly.FromDateTime(DateTime.UtcNow) > warranty.EndOn)
            return new SaveResult(FlashType: "error", FlashMessage: "Claims are only possible while the warranty is active.");
        var claim = new WarrantyClaim { WarrantyId = id, Issue = issue.Trim() };
        db.WarrantyClaims.Add(claim);
        db.Alerts.Add(new Alert
        {
            UserId = uid,
            Kind = AlertKinds.Warranty,
            Title = "Warranty claim submitted",
            Body = "Our team reviews warranty claims within 48 hours.",
            Href = "/account/warranties",
        });
        await db.SaveChangesAsync(ct);
        await realtime.NotifyUserAsync(uid, "Warranty claim submitted", "We will review it within 48 hours.", "/account/warranties", ct);
        return new SaveResult(FlashType: "success", FlashMessage: "Claim submitted.");
    }

    // ---------------------------------------------------------------- insurance

    internal static async Task<InsuranceData> GetInsuranceAsync(Guid uid, AppDb db, CancellationToken ct)
    {
        return new InsuranceData(
            await db.DeviceInsurances.AsNoTracking()
                .Where(d => d.UserId == uid)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new
                {
                    d.Id,
                    d.DeviceName,
                    d.DeviceSerial,
                    d.Premium,
                    d.CoverageAmount,
                    d.StartOn,
                    d.EndOn,
                    d.Status,
                    ProductSlug = db.Products.IgnoreQueryFilters().Where(p => p.Id == d.ProductId).Select(p => p.Slug).FirstOrDefault(),
                    Claims = db.InsuranceClaims.Where(c => c.DeviceInsuranceId == d.Id)
                        .Select(c => new InsuranceClaimRow(c.Id, c.IncidentType, c.Description, c.IncidentOn, c.PayoutAmount, c.Status, c.PaidAt, c.CreatedAt)).ToList(),
                })
                .ToListAsync(ct),
            // Attachable: delivered order items whose product is insurable and not already insured.
            await AttachableAsync(db, uid, ct));
    }

    internal static async Task<List<AttachableItem>> AttachableAsync(AppDb db, Guid uid, CancellationToken ct) =>
        await db.OrderItems.AsNoTracking()
            .Where(i => i.Order.UserId == uid && i.Order.Status == OrderStatuses.Delivered && i.ProductId != Guid.Empty)
            .Select(i => new
            {
                i.Id,
                i.Name,
                Slug = i.Product!.Slug,
                Already = db.DeviceInsurances.Any(d => d.UserId == uid && d.ProductId == i.ProductId),
            })
            .ToListAsync(ct)
            is { } rows
                ? rows.Select(r => new AttachableItem(r.Id, r.Name, r.Slug, r.Already)).ToList()
                : [];

    internal static async Task<SaveResult> ClaimInsuranceAsync(Guid uid, Guid id, string? incidentType, string? description, DateOnly? incidentOn, AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        if (incidentType is not (InsuranceIncidentTypes.Damage or InsuranceIncidentTypes.Theft or InsuranceIncidentTypes.Malfunction))
            return new SaveResult(Errors: new Dictionary<string, string> { ["incidentType"] = "Pick damage, theft or malfunction." });
        if (string.IsNullOrWhiteSpace(description))
            return new SaveResult(Errors: new Dictionary<string, string> { ["description"] = "Describe what happened." });
        var policy = await db.DeviceInsurances.FirstOrDefaultAsync(d => d.Id == id && d.UserId == uid, ct);
        if (policy is null)
            return new SaveResult(NotFound: true);
        if (policy.Status != InsuranceStatuses.Active)
            return new SaveResult(FlashType: "error", FlashMessage: "Claims require an active policy.");
        db.InsuranceClaims.Add(new InsuranceClaim
        {
            DeviceInsuranceId = id,
            IncidentType = incidentType,
            Description = description.Trim(),
            IncidentOn = incidentOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
        });
        db.Alerts.Add(new Alert
        {
            UserId = uid,
            Kind = AlertKinds.Insurance,
            Title = "Insurance claim submitted",
            Body = "We will review your claim and respond by email.",
            Href = "/account/insurance",
        });
        await db.SaveChangesAsync(ct);
        await realtime.NotifyUserAsync(uid, "Insurance claim submitted", "We will review your claim and respond by email.", "/account/insurance", ct);
        return new SaveResult(FlashType: "success", FlashMessage: "Claim submitted.");
    }

    /// Attaches a policy to an owned, delivered order item (purchased after checkout).
    internal static async Task<SaveResult> AttachInsuranceAsync(Guid uid, Guid orderItemId, AppDb db, CancellationToken ct)
    {
        var item = await db.OrderItems.AsNoTracking()
            .Where(i => i.Id == orderItemId && i.Order.UserId == uid && i.Order.Status == OrderStatuses.Delivered && i.ProductId != Guid.Empty)
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.ProductId,
                i.OrderId,
                i.Product!.IsInsurable,
                i.Product.Price,
                Already = db.DeviceInsurances.Any(d => d.UserId == uid && d.ProductId == i.ProductId),
                Insured = db.DeviceInsurances.Any(d => d.OrderId == i.OrderId),
            })
            .FirstOrDefaultAsync(ct);
        if (item is null)
            return new SaveResult(NotFound: true);
        if (!item.IsInsurable)
            return new SaveResult(FlashType: "error", FlashMessage: "That item does not offer device insurance.");
        if (item.Already || item.Insured)
            return new SaveResult(FlashType: "info", FlashMessage: "That item already has a policy.");
        var now = DateTime.UtcNow;
        db.DeviceInsurances.Add(new DeviceInsurance
        {
            UserId = uid,
            ProductId = item.ProductId,
            OrderId = item.OrderId,
            DeviceName = item.Name,
            Premium = Math.Round(item.Price * Pricing.InsurancePremiumPercent, 2, MidpointRounding.AwayFromZero),
            CoverageAmount = Math.Round(item.Price * Pricing.InsuranceCoveragePercent, 2, MidpointRounding.AwayFromZero),
            StartOn = DateOnly.FromDateTime(now),
            EndOn = DateOnly.FromDateTime(now).AddYears(1),
        });
        await db.SaveChangesAsync(ct);
        return new SaveResult(FlashType: "success", FlashMessage: "Policy attached.");
    }

    // ---------------------------------------------------------------- repairs

    internal static async Task<RepairsData> GetRepairsAsync(Guid uid, AppDb db, CancellationToken ct)
    {
        return new RepairsData(
            await db.RepairTickets.AsNoTracking()
                .Where(r => r.UserId == uid)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    r.Id,
                    r.DeviceName,
                    r.Problem,
                    r.Status,
                    r.QuotedCost,
                    r.TechnicianName,
                    r.AppointmentOn,
                    r.CreatedAt,
                    r.UpdatedAt,
                })
                .ToListAsync(ct),
            await db.Categories.AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync(ct));
    }

    internal static async Task<RepairDetailData?> GetRepairDetailAsync(Guid uid, Guid id, AppDb db, CancellationToken ct)
    {
        var ticket = await db.RepairTickets.AsNoTracking()
            .Where(r => r.Id == id && r.UserId == uid)
            .Select(r => new
            {
                r.Id,
                r.DeviceName,
                r.DeviceSerial,
                r.Problem,
                r.Status,
                r.QuotedCost,
                r.TechnicianName,
                r.AppointmentOn,
                r.CreatedAt,
                r.UpdatedAt,
                r.StatusHistoryJson,
            })
            .FirstOrDefaultAsync(ct);
        if (ticket is null)
            return null;
        return new RepairDetailData(
            ticket,
            RepairHistory.Read(ticket.StatusHistoryJson).Select(h => (object)new { h.Status, h.Note, h.At }).ToList());
    }

    internal static async Task<SaveResult> CreateRepairAsync(Guid uid, string? deviceName, string? problem, string? deviceSerial, Guid? categoryId, AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceName) || string.IsNullOrWhiteSpace(problem))
            return new SaveResult(Errors: new Dictionary<string, string>
            {
                [string.IsNullOrWhiteSpace(deviceName) ? "deviceName" : "problem"] = "Tell us what device broke and what happened.",
            });
        var now = DateTime.UtcNow;
        var ticket = new RepairTicket
        {
            UserId = uid,
            DeviceName = deviceName.Trim(),
            DeviceSerial = deviceSerial,
            Problem = problem.Trim(),
            CategoryId = categoryId,
            StatusHistoryJson = RepairHistory.Write([new RepairEvent(RepairStatuses.Received, "Ticket created", now)]),
        };
        db.RepairTickets.Add(ticket);
        db.Alerts.Add(new Alert
        {
            UserId = uid,
            Kind = AlertKinds.Repair,
            Title = "Repair ticket received",
            Body = "We will inspect it and send a quote.",
            Href = $"/account/repairs",
        });
        await db.SaveChangesAsync(ct);
        await realtime.NotifyUserAsync(uid, "Repair ticket received", "We will inspect it and send a quote.", "/account/repairs", ct);
        return new SaveResult(FlashType: "success", FlashMessage: "Repair ticket created.");
    }

    // ---------------------------------------------------------------- admin desk

    internal static async Task<AdminData> GetAdminDeskAsync(string? tab, AppDb db, CancellationToken ct)
    {
        tab ??= "warranty";
        var warranties = tab == "warranty"
            ? await db.WarrantyClaims.AsNoTracking()
                .OrderByDescending(c => c.CreatedAt).Take(100)
                .Select(c => new
                {
                    c.Id,
                    c.Issue,
                    c.Status,
                    c.DecisionNote,
                    c.CreatedAt,
                    c.UpdatedAt,
                    Item = c.Warranty.OrderItem.Name,
                    OrderNumber = c.Warranty.OrderItem.Order.Number,
                    Serial = c.Warranty.SerialNumber,
                    Customer = c.Warranty.User.Name,
                })
                .ToListAsync(ct)
            : [];
        var insurance = tab == "insurance"
            ? await db.InsuranceClaims.AsNoTracking()
                .OrderByDescending(c => c.CreatedAt).Take(100)
                .Select(c => new
                {
                    c.Id,
                    c.IncidentType,
                    c.Description,
                    c.IncidentOn,
                    c.PayoutAmount,
                    c.Status,
                    c.PaidAt,
                    c.CreatedAt,
                    Device = c.Policy.DeviceName,
                    Coverage = c.Policy.CoverageAmount,
                    Customer = c.Policy.User.Name,
                })
                .ToListAsync(ct)
            : [];
        var repairs = tab == "repairs"
            ? await db.RepairTickets.AsNoTracking()
                .OrderByDescending(t => t.CreatedAt).Take(100)
                .Select(t => new
                {
                    t.Id,
                    t.DeviceName,
                    t.DeviceSerial,
                    t.Problem,
                    t.Status,
                    t.QuotedCost,
                    t.TechnicianName,
                    t.AppointmentOn,
                    t.CreatedAt,
                    Customer = t.User.Name,
                    Email = t.User.Email,
                })
                .ToListAsync(ct)
            : [];
        return new AdminData(
            tab,
            warranties.Select(r => (object)r).ToList(),
            insurance.Select(r => (object)r).ToList(),
            repairs.Select(r => (object)r).ToList());
    }

    internal static async Task<SaveResult> DecideInsuranceClaimAsync(Guid id, string? decision, decimal? payout, string? note, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var claim = await db.InsuranceClaims.Include(c => c.Policy).ThenInclude(p => p.User).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null)
            return new SaveResult(NotFound: true);
        var userId = claim.Policy.UserId;
        switch (decision)
        {
            case "review":
                if (claim.Status != InsuranceClaimStatuses.Submitted) return WrongStateFlash();
                claim.Status = InsuranceClaimStatuses.UnderReview;
                break;
            case "approve":
                if (claim.Status is not (InsuranceClaimStatuses.Submitted or InsuranceClaimStatuses.UnderReview)) return WrongStateFlash();
                claim.Status = InsuranceClaimStatuses.Approved;
                break;
            case "pay":
                if (claim.Status != InsuranceClaimStatuses.Approved) return WrongStateFlash();
                claim.Status = InsuranceClaimStatuses.Paid;
                claim.PayoutAmount ??= claim.Policy.CoverageAmount;
                claim.PaidAt = DateTime.UtcNow;
                break;
            case "reject":
                if (claim.Status is InsuranceClaimStatuses.Paid) return WrongStateFlash();
                claim.Status = InsuranceClaimStatuses.Rejected;
                break;
            default:
                return new SaveResult(FlashType: "error", FlashMessage: "Unknown decision.");
        }
        claim.UpdatedAt = DateTime.UtcNow;
        db.Alerts.Add(new Alert
        {
            UserId = userId,
            Kind = AlertKinds.Insurance,
            Title = $"Claim is now {claim.Status}",
            Body = note ?? $"Decision: {claim.Status}.",
            Href = "/account/insurance",
        });
        await db.SaveChangesAsync(ct);
        jobs.Enqueue(db, new CareDecisionEmail("insurance-claim", id));
        await db.SaveChangesAsync(ct);
        await realtime.NotifyUserAsync(userId, "Insurance claim update",
            $"Your claim is now {claim.Status}.{(note is null ? "" : " " + note)}", "/account/insurance", ct);
        return new SaveResult();
    }

    internal static async Task<SaveResult> DecideWarrantyClaimAsync(Guid id, string? decision, string? note, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var claim = await db.WarrantyClaims.Include(c => c.Warranty).ThenInclude(w => w.User).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null)
            return new SaveResult(NotFound: true);
        var userId = claim.Warranty.UserId;
        switch (decision)
        {
            case "review":
                if (claim.Status != WarrantyClaimStatuses.Submitted) return WrongStateFlash();
                claim.Status = WarrantyClaimStatuses.InReview;
                break;
            case "approve":
                if (claim.Status is not (WarrantyClaimStatuses.Submitted or WarrantyClaimStatuses.InReview)) return WrongStateFlash();
                // Approved → replacement ships; the replacement rides on the original order's delivery.
                claim.Status = WarrantyClaimStatuses.Approved;
                break;
            case "ship":
                if (claim.Status != WarrantyClaimStatuses.Approved) return WrongStateFlash();
                claim.Status = WarrantyClaimStatuses.ReplacementShipped;
                break;
            case "resolve":
                if (claim.Status is not (WarrantyClaimStatuses.Approved or WarrantyClaimStatuses.ReplacementShipped)) return WrongStateFlash();
                claim.Status = WarrantyClaimStatuses.Resolved;
                break;
            case "reject":
                if (claim.Status is WarrantyClaimStatuses.Resolved) return WrongStateFlash();
                claim.Status = WarrantyClaimStatuses.Rejected;
                break;
            default:
                return new SaveResult(FlashType: "error", FlashMessage: "Unknown decision.");
        }
        claim.DecisionNote = note ?? claim.DecisionNote;
        claim.UpdatedAt = DateTime.UtcNow;
        db.Alerts.Add(new Alert
        {
            UserId = userId,
            Kind = AlertKinds.Warranty,
            Title = $"Warranty claim is now {claim.Status}",
            Body = note ?? $"Decision: {claim.Status}.",
            Href = "/account/warranties",
        });
        await db.SaveChangesAsync(ct);
        jobs.Enqueue(db, new CareDecisionEmail("warranty-claim", id));
        await db.SaveChangesAsync(ct);
        await realtime.NotifyUserAsync(userId, "Warranty claim update",
            $"Your claim is now {claim.Status}.{(note is null ? "" : " " + note)}", "/account/warranties", ct);
        return new SaveResult();
    }

    internal static async Task<SaveResult> UpdateRepairAsync(Guid id, string? status, decimal? quotedCost, string? technicianName, DateOnly? appointmentOn, string? note, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var ticket = await db.RepairTickets.Include(t => t.User).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null)
            return new SaveResult(NotFound: true);
        var changed = false;
        if (status is not null && status != ticket.Status)
        {
            if (!RepairStates.Contains(status))
                return new SaveResult(FlashType: "error", FlashMessage: "Unknown repair status.");
            // Repairs only move forward in the flow.
            if (RepairFlow.IndexOf(ticket.Status) >= RepairFlow.IndexOf(status))
                return new SaveResult(FlashType: "error", FlashMessage: $"Repairs never go from {ticket.Status} to {status}.");
            ticket.Status = status;
            ticket.StatusHistoryJson = RepairHistory.Append(ticket.StatusHistoryJson,
                new RepairEvent(status, note ?? $"Status set to {status}", DateTime.UtcNow));
            changed = true;
        }
        if (quotedCost is { } cost) { ticket.QuotedCost = cost; changed = true; }
        if (!string.IsNullOrWhiteSpace(technicianName)) { ticket.TechnicianName = technicianName.Trim(); changed = true; }
        if (appointmentOn is { } when) { ticket.AppointmentOn = when; changed = true; }
        if (!changed) return new SaveResult(FlashType: "info", FlashMessage: "Nothing to update.");
        ticket.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        jobs.Enqueue(db, new CareDecisionEmail("repair", id));
        await realtime.NotifyUserAsync(ticket.UserId, "Repair update",
            $"{ticket.DeviceName}: now {ticket.Status}.", "/account/repairs", ct);
        return new SaveResult();
    }

    static SaveResult WrongStateFlash() => new(FlashType: "error", FlashMessage: "That move is not allowed for this claim.");

    static readonly string[] RepairStates =
        [RepairStatuses.Received, RepairStatuses.Quoted, RepairStatuses.Repairing, RepairStatuses.Ready, RepairStatuses.PickedUp, RepairStatuses.Closed];
    static readonly List<string> RepairFlow = [.. RepairStates];
}

public sealed record ClaimRow(Guid Id, string Issue, string Status, string? DecisionNote, DateTime CreatedAt);
public sealed record InsuranceClaimRow(Guid Id, string IncidentType, string Description, DateOnly IncidentOn, decimal? PayoutAmount, string Status, DateTime? PaidAt, DateTime CreatedAt);
public sealed record AttachableItem(Guid Id, string Name, string Slug, bool Already);

public static class InsuranceIncidentTypes
{
    public const string Damage = "damage";
    public const string Theft = "theft";
    public const string Malfunction = "malfunction";
}
