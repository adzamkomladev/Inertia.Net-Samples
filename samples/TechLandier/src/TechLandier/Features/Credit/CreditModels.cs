using TechLandier.Data;
namespace TechLandier.Features;

public static class PayPlanStatuses
{
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Defaulted = "defaulted";
    public const string Cancelled = "cancelled";
}

public static class InstallmentStatuses
{
    public const string Due = "due";
    public const string Paid = "paid";
    public const string Overdue = "overdue";
    public const string Waived = "waived";
}

public sealed class PayPlan
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// One plan per order — BNPL covers the whole order or nothing.
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    /// 3 | 6 | 12 monthly-equivalent installments, spaced 14 days apart.
    public int InstallmentCount { get; set; }
    public decimal AmountPerInstallment { get; set; }
    /// Total fee baked into the schedule: Total * BnplFeePercent.
    public decimal FeeTotal { get; set; }
    public string Status { get; set; } = PayPlanStatuses.Active;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? DemoKey { get; set; }
}

public sealed class Installment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PlanId { get; set; }
    public PayPlan Plan { get; set; } = null!;
    public int Number { get; set; }
    public decimal Amount { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? PaymentRef { get; set; }
    public string Status { get; set; } = InstallmentStatuses.Due;
}
