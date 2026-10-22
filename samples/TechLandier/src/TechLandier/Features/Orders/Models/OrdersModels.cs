using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public sealed record CheckoutInput
{
    [Required]
    public Guid? ProductId { get; init; }
    [Range(1, 10)]
    public int Qty { get; init; } = 1;
    public Guid? ZoneId { get; init; }
    /// card | mobile_money | pay_on_delivery
    [Required]
    public string? PaymentMethod { get; init; }
    /// null = pay in full; 3, 6 or 12 = installments every 14 days.
    public int? BnplCount { get; init; }
    /// Device insurance add-on for insurable products.
    public bool Insurance { get; init; }
    [Required, StringLength(80)]
    public string? RecipientName { get; init; }
    [Required, StringLength(24)]
    public string? Phone { get; init; }
    [Required, StringLength(120)]
    public string? StreetLabel { get; init; }
    public string? Notes { get; init; }
    /// Idempotency key from the client; a retry with the same key returns the same order.
    [Required, StringLength(200)]
    public string? CheckoutAttemptId { get; init; }
}

public sealed record OrderItemRow(Guid Id, Guid ProductId, string Name, string Sku, string ImageArt, decimal UnitPrice, int Qty, decimal LineTotal, string? Slug);

public sealed record OrderDetail(
    Guid Id, string Number, string Status, string PaymentStatus, string PaymentMethod, decimal Subtotal, decimal DiscountTotal,
    decimal ShippingFee, decimal Total, DateTime? PaidAt, DateTime? ShippedAt, DateTime? DeliveredAt, DateTime? CancelledAt,
    string? CancelReason, string? ReceiptNumber, DateTime CreatedAt, List<OrderItemRow> Items);

public sealed record OrderEventRow(Guid Id, string Status, string? Note, DateTime At);

public sealed record DeliveryView(
    string Status, decimal? FeeCollected, decimal? CashCollected, string? FailedReason, DateTime? AssignedAt, DateTime? PickedUpAt,
    DateTime? DeliveredAt, string? RiderName, string? RiderPhone, string? Vehicle, string? Service, string? ZoneName, Guid? ZoneId, string? ZoneEta);

public sealed record WarrantyView(Guid Id, string ItemName, string? SerialNumber, DateOnly StartOn, DateOnly EndOn, string Status);
public sealed record InsuranceView(Guid Id, Guid? ProductId, string DeviceName, string? DeviceSerial, decimal Premium, decimal CoverageAmount, DateOnly StartOn, DateOnly EndOn, string Status);
public sealed record ReviewCta(string Slug, string Name, bool Eligible);
public sealed record PaymentRow(Guid Id, string Method, string? ProviderTxRef, decimal Amount, string Status, DateTime? PaidAt);
public sealed record RiderRow(Guid Id, string Name, string Phone, string? Vehicle, string Service);
public sealed record OrderCustomer(string Name, string Phone, string Street, string? Notes, string Email);

public sealed record AccountOrderRow(Guid Id, string Number, string Status, string PaymentStatus, string PaymentMethod, decimal Total, DateTime CreatedAt, List<string> ItemNames, bool HasReview);

public sealed record DeliveryBrief(string Status, string? RiderName);
public sealed record AdminOrderRow(
    Guid Id, string Number, string Status, string PaymentStatus, string PaymentMethod, decimal Total, DateTime CreatedAt,
    string CustomerName, string CustomerEmail, DeliveryBrief? Delivery)
{
    public bool Unassigned => Delivery?.Status == DeliveryStatuses.Unassigned;
}

public sealed record MoveOrderInput(string? To, string? Note);
public sealed record RefundInput(string? Note);
