using TechLandier.Data;
namespace TechLandier.Features;

public static class OrderStatuses
{
    public const string Pending = "pending";
    public const string Paid = "paid";
    public const string Processing = "processing";
    public const string Shipped = "shipped";
    public const string Delivered = "delivered";
    public const string Cancelled = "cancelled";

    public static readonly string[] PaidOrLater = [Paid, Processing, Shipped, Delivered];
}

public static class PaymentMethods
{
    public const string Card = "card";
    public const string MobileMoney = "mobile_money";
    public const string PayOnDelivery = "pay_on_delivery";
}

public static class PaymentStatuses
{
    public const string Unpaid = "unpaid";
    public const string Pending = "pending";
    public const string Paid = "paid";
    public const string Failed = "failed";
    public const string Refunded = "refunded";
}

public sealed class Order
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// Human order number, TL-2026-00001 style, formatted by OrderNumbers.New().
    public required string Number { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Status { get; set; } = OrderStatuses.Pending;
    public string PaymentMethod { get; set; } = PaymentMethods.Card;
    public string PaymentStatus { get; set; } = PaymentStatuses.Unpaid;
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal Total { get; set; }
    public Guid? ZoneId { get; set; }
    public DeliveryZone? Zone { get; set; }
    /// Snapshot of the address at checkout — never re-linked to live state.
    public OrderAddress AddressSnapshot { get; set; } = new();
    public string? Notes { get; set; }
    /// Idempotency key: re-submitting checkout with the same key returns the same order.
    public required string CheckoutAttemptId { get; set; }
    /// Short unguessable token so guests can follow the tracking page via /orders/{number}?t=.
    public string AccessToken { get; set; } = "";
    public string? ReceiptNumber { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? DemoKey { get; set; }
    public ICollection<OrderItem> Items { get; set; } = [];
}

public sealed class OrderAddress
{
    public string RecipientName { get; set; } = "";
    public string Phone { get; set; } = "";
    /// GhanaPost GPS style free text (e.g. "GA-183-5287 Nyaneba Avenue").
    public string StreetLabel { get; set; } = "";
}

public sealed class OrderItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    /// Display name copied at checkout so history survives product edits.
    public required string Name { get; set; }
    public required string Sku { get; set; }
    /// Deterministic art meta "lucideIcon:Brand" so order history renders without S3 images.
    public required string ImageArt { get; set; }
    /// Unit price actually charged, i.e. Pricing.Display(Product.Price, DiscountPercent) at checkout.
    public decimal UnitPrice { get; set; }
    public int Qty { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class OrderEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public required string Status { get; set; }
    public string? Note { get; set; }
    public Guid? ByUserId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public sealed class Payment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public string Method { get; set; } = PaymentMethods.Card;
    /// Mock provider reference in dev (simulated gateway); real providers would replace this.
    public string? ProviderTxRef { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = Pricing.Currency;
    public string Status { get; set; } = PaymentStatuses.Pending;
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
