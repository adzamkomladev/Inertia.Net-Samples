using TechLandier.Data;
namespace TechLandier.Features;

public static class AlertKinds
{
    public const string OrderStatus = "order_status";
    public const string InstallmentDue = "installment_due";
    public const string Overdue = "overdue";
    public const string Knowledge = "knowledge";
    public const string Reply = "reply";
    public const string Insurance = "insurance";
    public const string Warranty = "warranty";
    public const string Repair = "repair";
    public const string Promo = "promo";
}

public sealed class Alert
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public required string Kind { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    /// In-app destination, e.g. "/account/orders/{id}".
    public string? Href { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
