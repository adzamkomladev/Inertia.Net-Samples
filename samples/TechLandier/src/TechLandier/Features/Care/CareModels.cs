using TechLandier.Data;
using System.Text.Json;

namespace TechLandier.Features;

public static class WarrantyStatuses
{
    public const string Active = "active";
    public const string Expired = "expired";
    public const string Void = "void";
}

public static class WarrantyClaimStatuses
{
    public const string Submitted = "submitted";
    public const string InReview = "in_review";
    public const string Approved = "approved";
    public const string ReplacementShipped = "replacement_shipped";
    public const string Rejected = "rejected";
    public const string Resolved = "resolved";
}

public static class InsuranceStatuses
{
    public const string Active = "active";
    public const string Expired = "expired";
    public const string Void = "void";
}

public static class InsuranceClaimStatuses
{
    public const string Submitted = "submitted";
    public const string UnderReview = "under_review";
    public const string Approved = "approved";
    public const string Paid = "paid";
    public const string Rejected = "rejected";
}

public static class RepairStatuses
{
    public const string Received = "received";
    public const string Quoted = "quoted";
    public const string Repairing = "repairing";
    public const string Ready = "ready";
    public const string PickedUp = "picked_up";
    public const string Closed = "closed";
}

public sealed class Warranty
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// Auto-created in checkout for items whose product WarrantyMonths > 0; one per order item.
    public Guid OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    /// Customer-editable device serial; blank until they set it.
    public string? SerialNumber { get; set; }
    public DateOnly StartOn { get; set; }
    public DateOnly EndOn { get; set; }
    public string Status { get; set; } = WarrantyStatuses.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? DemoKey { get; set; }
}

public sealed class WarrantyClaim
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid WarrantyId { get; set; }
    public Warranty Warranty { get; set; } = null!;
    public required string Issue { get; set; }
    public string Status { get; set; } = WarrantyClaimStatuses.Submitted;
    public string? DecisionNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class DeviceInsurance
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    /// The order that introduced the device, when bought at checkout or attached later.
    public Guid? OrderId { get; set; }
    public Order? Order { get; set; }
    /// Insurable products only (IsInsurable).
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public required string DeviceName { get; set; }
    public string? DeviceSerial { get; set; }
    /// Fixed 12-month cover priced at Pricing.InsurancePremiumPercent of the device price.
    public decimal Premium { get; set; }
    /// Payout on approved claim = Pricing.InsuranceCoveragePercent of the device price.
    public decimal CoverageAmount { get; set; }
    public DateOnly StartOn { get; set; }
    public DateOnly EndOn { get; set; }
    public string Status { get; set; } = InsuranceStatuses.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? DemoKey { get; set; }
}

public sealed class InsuranceClaim
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid DeviceInsuranceId { get; set; }
    public DeviceInsurance Policy { get; set; } = null!;
    /// damage | theft | malfunction.
    public required string IncidentType { get; set; }
    public required string Description { get; set; }
    public DateOnly IncidentOn { get; set; }
    public decimal? PayoutAmount { get; set; }
    public string Status { get; set; } = InsuranceClaimStatuses.Submitted;
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class RepairTicket
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public required string DeviceName { get; set; }
    public string? DeviceSerial { get; set; }
    public required string Problem { get; set; }
    /// Optional hint, e.g. the product category the device belongs to.
    public Guid? CategoryId { get; set; }
    public string Status { get; set; } = RepairStatuses.Received;
    public decimal? QuotedCost { get; set; }
    public string? TechnicianName { get; set; }
    public DateOnly? AppointmentOn { get; set; }
    /** [{"status":"received","note":"...","at":"..."}] */
    public string StatusHistoryJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// One status change in a repair ticket's flat history list.
public sealed record RepairEvent(string Status, string? Note, DateTime At);

public static class RepairHistory
{
    public static List<RepairEvent> Read(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : (JsonSerializer.Deserialize<List<RepairEvent>>(json) ?? []);

    public static string Write(IReadOnlyList<RepairEvent> events) =>
        JsonSerializer.Serialize(events);

    public static string Append(string json, RepairEvent @event)
    {
        var events = Read(json);
        events.Add(@event);
        return Write(events);
    }
}

public static class InsuranceIncidentTypes
{
    public const string Damage = "damage";
    public const string Theft = "theft";
    public const string Malfunction = "malfunction";
}

public sealed record ClaimRow(Guid Id, string Issue, string Status, string? DecisionNote, DateTime CreatedAt);
public sealed record WarrantyListRow(Guid Id, string ItemName, string OrderNumber, string? SerialNumber, DateOnly StartOn, DateOnly EndOn, string Status, List<ClaimRow> Claims);
public sealed record InsuranceClaimRow(Guid Id, string IncidentType, string Description, DateOnly IncidentOn, decimal? PayoutAmount, string Status, DateTime? PaidAt, DateTime CreatedAt);
public sealed record PolicyRow(Guid Id, string DeviceName, string? DeviceSerial, decimal Premium, decimal CoverageAmount, DateOnly StartOn, DateOnly EndOn, string Status, string ProductSlug, List<InsuranceClaimRow> Claims);
public sealed record AttachableItem(Guid Id, string Name, string Slug, bool Already);
public sealed record InsuranceData(List<PolicyRow> Policies, List<AttachableItem> Attachable);
public sealed record RepairListRow(Guid Id, string DeviceName, string Problem, string Status, decimal? QuotedCost, string? TechnicianName, DateOnly? AppointmentOn, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record RepairDetail(Guid Id, string DeviceName, string? DeviceSerial, string Problem, string Status, decimal? QuotedCost, string? TechnicianName, DateOnly? AppointmentOn, DateTime CreatedAt, DateTime UpdatedAt, string StatusHistoryJson);
public sealed record CategoryOption(Guid Id, string Name);
public sealed record RepairsData(List<RepairListRow> Repairs, List<CategoryOption> Categories);
public sealed record RepairDetailData(RepairDetail Repair, List<RepairEvent> History);

public sealed record WarrantyClaimAdminRow(Guid Id, string Issue, string Status, string? DecisionNote, DateTime CreatedAt, DateTime UpdatedAt, string Item, string OrderNumber, string? Serial, string Customer);
public sealed record InsuranceClaimAdminRow(Guid Id, string IncidentType, string Description, DateOnly IncidentOn, decimal? PayoutAmount, string Status, DateTime? PaidAt, DateTime CreatedAt, string Device, decimal Coverage, string Customer);
public sealed record RepairAdminRow(Guid Id, string DeviceName, string? DeviceSerial, string Problem, string Status, decimal? QuotedCost, string? TechnicianName, DateOnly? AppointmentOn, DateTime CreatedAt, string Customer, string Email);

public sealed record SerialInput(string? SerialNumber);
public sealed record WarrantyClaimInput(string? Issue);
public sealed record InsuranceClaimInput(string? IncidentType, string? Description, DateOnly? IncidentOn);
public sealed record RepairInput(string? DeviceName, string? DeviceSerial, string? Problem, string? CategoryId);
public sealed record WarrantyDecisionInput(string? Decision, string? Note);
public sealed record InsuranceDecisionInput(string? Decision, decimal? Payout, string? Note);
public sealed record RepairUpdateInput(string? Status, decimal? QuotedCost, string? TechnicianName, DateOnly? AppointmentOn, string? Note);

public sealed record CareCaseDetail(Guid Id, string Kind, string Device, string Customer, string Email, string? Serial,
    string Description, string Status, DateTime CreatedAt, DateTime UpdatedAt, string? OrderNumber,
    DateOnly? StartOn, DateOnly? EndOn, decimal? Coverage, decimal? Amount, string? Note,
    string? Technician, DateOnly? AppointmentOn, List<RepairEvent> History);
