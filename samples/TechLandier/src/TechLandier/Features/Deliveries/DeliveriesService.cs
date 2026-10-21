using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// Admin delivery board + riders/zones managers. POD cash reconciles when a rider marks "delivered".
/// Returns plain data + SaveResult outcomes; the Deliveries.cs route handlers compose the Inertia responses.
public static partial class DeliveriesService
{
    public sealed record BoardData(List<DeliveryRow> Deliveries, List<RiderAdminRow> Riders, List<ZoneAdminRow> Zones);

    // ---------------------------------------------------------------- board

    internal static async Task<BoardData> GetBoardAsync(AppDb db, CancellationToken ct)
    {
        var deliveries = await db.Deliveries.AsNoTracking()
            .Where(d => d.Order.Status != OrderStatuses.Cancelled)
            .OrderBy(d => d.CreatedAt)
            .Select(d => new DeliveryRow(
                d.Id,
                d.Order.Number,
                d.Status,
                d.Zone != null ? d.Zone.Name : null,
                d.Rider != null ? d.Rider.Name : null,
                d.Order.PaymentMethod,
                d.Order.Total,
                d.CashCollected,
                d.FailedReason,
                d.DeliveredAt))
            .ToListAsync(ct);
        var riders = await db.Riders.OrderBy(r => r.Name).ToListAsync(ct);
        var zones = await db.DeliveryZones.OrderBy(z => z.SortOrder).ToListAsync(ct);
        return new BoardData(
            deliveries,
            riders.Select(r => new RiderAdminRow(r.Id, r.Name, r.Phone, r.Vehicle, r.Service, r.IsActive)).ToList(),
            zones.Select(z => new ZoneAdminRow(z.Id, z.Name, z.Fee, z.EtaText, z.AllowPayOnDelivery, z.SortOrder, z.IsActive)).ToList());
    }

    internal static async Task<SaveResult> AssignRiderAsync(Guid id, Guid? riderId, AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        var delivery = await db.Deliveries.Include(d => d.Order).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (delivery is null)
            return new SaveResult(NotFound: true);
        if (delivery.Status is DeliveryStatuses.Delivered)
            return new SaveResult(FlashType: "error", FlashMessage: "That delivery is already complete.");
        if (riderId is not { } wanted)
            return new SaveResult(Errors: new Dictionary<string, string> { ["riderId"] = "Pick a rider." });
        var rider = await db.Riders.FirstOrDefaultAsync(r => r.Id == wanted, ct);
        if (rider is null || !rider.IsActive)
            return new SaveResult(Errors: new Dictionary<string, string> { ["riderId"] = "That rider is inactive." });

        delivery.RiderId = rider.Id;
        // A picked-up run that gets reassigned keeps its pickup state; failed/unassigned become assigned.
        delivery.Status = delivery.PickedUpAt is not null ? DeliveryStatuses.PickedUp : DeliveryStatuses.Assigned;
        delivery.AssignedAt = DateTime.UtcNow;
        delivery.FailedReason = null;
        await db.SaveChangesAsync(ct);
        await realtime.OrderTimelineUpdatedAsync(delivery.Order.UserId, delivery.Order.Number, ct);
        return new SaveResult(FlashType: "success", FlashMessage: $"{rider.Name} assigned to {delivery.Order.Number}.");
    }

    internal static async Task<SaveResult> PickupAsync(Guid id, AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        var delivery = await db.Deliveries.Include(d => d.Order).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (delivery is null)
            return new SaveResult(NotFound: true);
        if (delivery.Status != DeliveryStatuses.Assigned)
            return new SaveResult(FlashType: "error", FlashMessage: "Assign a rider before pickup.");
        delivery.Status = DeliveryStatuses.PickedUp;
        delivery.PickedUpAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await realtime.OrderTimelineUpdatedAsync(delivery.Order.UserId, delivery.Order.Number, ct);
        return new SaveResult(FlashType: "success", FlashMessage: "Pickup confirmed.");
    }

    /// Marking delivered: POD riders hand in the order-total cash and the order lifecycle closes here.
    internal static async Task<SaveResult> DeliverAsync(Guid id, decimal? cash, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var delivery = await db.Deliveries.Include(d => d.Order).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (delivery is null)
            return new SaveResult(NotFound: true);
        if (delivery.Status is DeliveryStatuses.Delivered)
            return new SaveResult();
        if (delivery.RiderId is null)
            return new SaveResult(FlashType: "error", FlashMessage: "Assign a rider before marking delivered.");

        var at = DateTime.UtcNow;
        delivery.Status = DeliveryStatuses.Delivered;
        delivery.DeliveredAt = at;
        var order = delivery.Order;

        if (order.PaymentMethod == PaymentMethods.PayOnDelivery)
        {
            delivery.CashCollected = cash ?? order.Total;
            delivery.FeeCollected = order.ShippingFee;
            var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == order.Id && p.Status == PaymentStatuses.Pending, ct);
            if (payment is { } pod)
            {
                pod.Status = PaymentStatuses.Paid;
                pod.PaidAt = at;
                pod.ProviderTxRef = $"pod-cash-{delivery.Id.ToString("N")[..8]}";
            }
            // POD orders never "ship": workflow confirms paid (receipt email + pulses), then we close it out.
            if (order.Status == OrderStatuses.Pending)
                await OrderService.MoveAsync(db, order, OrderStatuses.Paid, "Cash collected on delivery", null, jobs, realtime, ct);
            if (order.Status != OrderStatuses.Delivered)
            {
                order.Status = OrderStatuses.Delivered;
                order.DeliveredAt = at;
                order.UpdatedAt = at;
                db.OrderEvents.Add(new OrderEvent
                { OrderId = order.Id, Status = OrderStatuses.Delivered, Note = "Handed over by rider after cash payment", At = at });
            }
        }
        else if (order.Status != OrderStatuses.Delivered)
        {
            await OrderService.MoveAsync(db, order, OrderStatuses.Delivered, "Delivered by rider", null, jobs, realtime, ct);
        }
        await db.SaveChangesAsync(ct);
        await realtime.OrderChangedAsync(order.Id, ct);
        await realtime.OrderTimelineUpdatedAsync(order.UserId, order.Number, ct);
        return new SaveResult(FlashType: "success", FlashMessage: $"Delivery for {order.Number} completed.");
    }

    internal static async Task<SaveResult> FailAsync(Guid id, string? reason, AppDb db, RealtimeEvents realtime, CancellationToken ct)
    {
        var delivery = await db.Deliveries.Include(d => d.Order).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (delivery is null)
            return new SaveResult(NotFound: true);
        if (delivery.Status is DeliveryStatuses.Delivered)
            return new SaveResult();
        if (string.IsNullOrWhiteSpace(reason))
            return new SaveResult(Errors: new Dictionary<string, string> { ["reason"] = "Say why the attempt failed." });
        delivery.Status = DeliveryStatuses.Failed;
        delivery.FailedReason = reason.Trim();
        await db.SaveChangesAsync(ct);
        await realtime.OrderTimelineUpdatedAsync(delivery.Order.UserId, delivery.Order.Number, ct);
        return new SaveResult(FlashType: "info", FlashMessage: "Delivery marked failed — reassign when ready.");
    }

    // ---------------------------------------------------------------- riders manager

    internal static async Task<SaveResult> CreateRiderAsync(string? name, string? phone, string? vehicle, string? service, AppDb db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2)
            return new SaveResult(Errors: new Dictionary<string, string> { ["name"] = "Rider name is required." });
        if (string.IsNullOrWhiteSpace(phone))
            return new SaveResult(Errors: new Dictionary<string, string> { ["phone"] = "Rider phone is required." });
        db.Riders.Add(new Rider
        {
            Name = name.Trim(),
            Phone = phone.Trim(),
            Vehicle = vehicle?.Trim(),
            Service = service is RiderServices.InHouse or RiderServices.Yango or RiderServices.Bolt or RiderServices.Uber
                ? service : RiderServices.InHouse,
        });
        await db.SaveChangesAsync(ct);
        return new SaveResult(FlashType: "success", FlashMessage: "Rider added.");
    }

    internal static async Task<SaveResult> UpdateRiderAsync(Guid id, string? name, string? phone, string? vehicle, string? service, bool? isActive, AppDb db, CancellationToken ct)
    {
        var rider = await db.Riders.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rider is null)
            return new SaveResult(NotFound: true);
        if (!string.IsNullOrWhiteSpace(name)) rider.Name = name.Trim();
        if (!string.IsNullOrWhiteSpace(phone)) rider.Phone = phone.Trim();
        if (vehicle is not null) rider.Vehicle = vehicle.Trim();
        if (service is RiderServices.InHouse or RiderServices.Yango or RiderServices.Bolt or RiderServices.Uber or RiderServices.Other)
            rider.Service = service;
        if (isActive is { } flag) rider.IsActive = flag;
        await db.SaveChangesAsync(ct);
        return new SaveResult(FlashType: "success", FlashMessage: "Rider updated.");
    }

    // ---------------------------------------------------------------- zones manager

    internal static async Task<SaveResult> CreateZoneAsync(string? name, decimal? fee, string? etaText, bool? allowPayOnDelivery, AppDb db, CancellationToken ct)
    {
        var failures = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(name)) failures["name"] = "Zone name is required.";
        if (fee is null || fee.Value < 0) failures["fee"] = "Fee must be zero or more.";
        if (string.IsNullOrWhiteSpace(etaText)) failures["etaText"] = "Give customers an ETA (e.g. \"Same day if ordered by 2pm\").";
        if (failures.Count > 0) return new SaveResult(Errors: failures);
        db.DeliveryZones.Add(new DeliveryZone
        {
            Name = name!.Trim(),
            Fee = fee!.Value,
            EtaText = etaText!.Trim(),
            AllowPayOnDelivery = allowPayOnDelivery ?? false,
            SortOrder = (await db.DeliveryZones.MaxAsync(z => (int?)z.SortOrder, ct)) + 1 ?? 0,
        });
        await db.SaveChangesAsync(ct);
        return new SaveResult(FlashType: "success", FlashMessage: "Zone added.");
    }

    internal static async Task<SaveResult> UpdateZoneAsync(Guid id, string? name, decimal? fee, string? etaText, bool? allowPayOnDelivery, bool? isActive, AppDb db, CancellationToken ct)
    {
        var zone = await db.DeliveryZones.FirstOrDefaultAsync(z => z.Id == id, ct);
        if (zone is null)
            return new SaveResult(NotFound: true);
        if (!string.IsNullOrWhiteSpace(name)) zone.Name = name.Trim();
        if (fee is { } feeValue && feeValue >= 0) zone.Fee = feeValue;
        if (!string.IsNullOrWhiteSpace(etaText)) zone.EtaText = etaText.Trim();
        if (allowPayOnDelivery is { } pod) zone.AllowPayOnDelivery = pod;
        if (isActive is { } active) zone.IsActive = active;
        await db.SaveChangesAsync(ct);
        return new SaveResult(FlashType: "success", FlashMessage: "Zone updated.");
    }
}

public sealed record DeliveryRow(Guid Id, string OrderNumber, string Status, string? Zone, string? Rider, string PaymentMethod, decimal Total, decimal? CashCollected, string? FailedReason, DateTime? DeliveredAt);

public sealed record RiderAdminRow(Guid Id, string Name, string Phone, string? Vehicle, string Service, bool IsActive);

public sealed record ZoneAdminRow(Guid Id, string Name, decimal Fee, string EtaText, bool AllowPayOnDelivery, int SortOrder, bool IsActive);
