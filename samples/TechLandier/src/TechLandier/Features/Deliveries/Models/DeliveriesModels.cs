using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public sealed record DeliveryRow(Guid Id, string OrderNumber, string Status, string? Zone, string? Rider, string PaymentMethod, decimal Total, decimal? CashCollected, string? FailedReason, DateTime? DeliveredAt);
public sealed record RiderAdminRow(Guid Id, string Name, string Phone, string? Vehicle, string Service, bool IsActive);
public sealed record ZoneAdminRow(Guid Id, string Name, decimal Fee, string EtaText, bool AllowPayOnDelivery, int SortOrder, bool IsActive);
public sealed record BoardData(List<DeliveryRow> Deliveries, List<RiderAdminRow> Riders, List<ZoneAdminRow> Zones);

public sealed record AssignInput(string? RiderId);
public sealed record DeliverInput(decimal? Cash);
public sealed record FailInput(string? Reason);
public sealed record RiderInput(string? Name, string? Phone, string? Vehicle, string? Service, bool? IsActive);
public sealed record ZoneInput(string? Name, decimal? Fee, string? EtaText, bool? AllowPayOnDelivery, bool? IsActive);
