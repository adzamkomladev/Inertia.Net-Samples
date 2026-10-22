namespace TechLandier.Features;

public static class DeliveryStatuses
{
    public const string Unassigned = "unassigned";
    public const string Assigned = "assigned";
    public const string PickedUp = "picked_up";
    public const string Delivered = "delivered";
    public const string Failed = "failed";
}

public static class RiderServices
{
    public const string InHouse = "in_house";
    public const string Yango = "yango";
    public const string Bolt = "bolt";
    public const string Uber = "uber";
    public const string Other = "other";
}

public sealed class DeliveryZone
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public decimal Fee { get; set; }
    /// "Same day if ordered by 2pm" / "1–2 days" — shown at checkout.
    public required string EtaText { get; set; }
    public bool AllowPayOnDelivery { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? DemoKey { get; set; }
}

public sealed class Rider
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string Phone { get; set; }
    public string? Vehicle { get; set; }
    public string Service { get; set; } = RiderServices.InHouse;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? DemoKey { get; set; }
}

public sealed class Delivery
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// One delivery per order; created when the order ships (or right away for pay-on-delivery).
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid? ZoneId { get; set; }
    public DeliveryZone? Zone { get; set; }
    public Guid? RiderId { get; set; }
    public Rider? Rider { get; set; }
    public string Status { get; set; } = DeliveryStatuses.Unassigned;
    /// Delivery fee cash the rider holds — reconciled on delivered for pay-on-delivery orders.
    public decimal? FeeCollected { get; set; }
    /// Order total cash collected by the rider when PaymentMethod = pay_on_delivery.
    public decimal? CashCollected { get; set; }
    public string? FailedReason { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime? PickedUpAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
