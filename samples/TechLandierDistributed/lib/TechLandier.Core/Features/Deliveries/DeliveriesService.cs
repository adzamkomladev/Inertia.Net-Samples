using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// Admin delivery board plus the riders and zones managers. Pay-on-delivery cash reconciles when a rider marks "delivered".
public sealed class DeliveriesService(AppDb db, OrderService orders, RealtimeEvents realtime)
{
    static readonly string[] RiderServiceNames = [RiderServices.InHouse, RiderServices.Yango, RiderServices.Bolt, RiderServices.Uber, RiderServices.Other];

    // ---------------------------------------------------------------- board

    public async Task<BoardData> BoardAsync(CancellationToken ct)
    {
        var deliveries = await db.Deliveries.AsNoTracking()
            .Where(d => d.Order.Status != OrderStatuses.Cancelled)
            .OrderBy(d => d.CreatedAt)
            .Select(d => new DeliveryRow(d.Id, d.Order.Number, d.Status, d.ZoneId, d.Zone != null ? d.Zone.Name : null, d.RiderId, d.Rider != null ? d.Rider.Name : null,
                d.Order.AddressSnapshot.RecipientName, d.Order.AddressSnapshot.Phone, d.Order.AddressSnapshot.StreetLabel,
                d.Order.PaymentMethod, d.Order.Total, d.CashCollected, d.FailedReason, d.Attempts, d.Note, d.ReceivedBy,
                d.CreatedAt, d.AssignedAt, d.PickedUpAt, d.DeliveredAt))
            .ToListAsync(ct);
        var since = DateTime.UtcNow.AddDays(-30);
        var riders = await db.Riders.AsNoTracking().OrderBy(r => r.Name)
            .Select(r => new RiderAdminRow(r.Id, r.Name, r.Phone, r.Vehicle, r.Service, r.IsActive, r.Capacity,
                db.Deliveries.Count(d => d.RiderId == r.Id && (d.Status == DeliveryStatuses.Assigned || d.Status == DeliveryStatuses.PickedUp)),
                db.Deliveries.Count(d => d.RiderId == r.Id && d.Status == DeliveryStatuses.Delivered && d.DeliveredAt >= since)))
            .ToListAsync(ct);
        var zones = await db.DeliveryZones.AsNoTracking().OrderBy(z => z.SortOrder)
            .Select(z => new ZoneAdminRow(z.Id, z.Name, z.Fee, z.EtaText, z.AllowPayOnDelivery, z.SortOrder, z.IsActive)).ToListAsync(ct);
        return new BoardData(deliveries, riders, zones);
    }

    public async Task<SaveResult> AssignRiderAsync(Guid id, AssignInput input, CancellationToken ct)
    {
        var delivery = await db.Deliveries.Include(d => d.Order).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (delivery is null) return SaveResult.Missing;
        if (delivery.Status == DeliveryStatuses.Delivered) return SaveResult.Error("That delivery is already complete.");
        if (!Guid.TryParse(input.RiderId, out var riderId)) return SaveResult.Invalid("riderId", "Pick a rider.");
        var rider = await db.Riders.FirstOrDefaultAsync(r => r.Id == riderId && r.IsActive, ct);
        if (rider is null) return SaveResult.Invalid("riderId", "That rider is inactive.");

        delivery.RiderId = rider.Id;
        // A picked-up run that gets reassigned keeps its pickup state; failed or unassigned runs become assigned.
        delivery.Status = delivery.PickedUpAt is not null ? DeliveryStatuses.PickedUp : DeliveryStatuses.Assigned;
        delivery.AssignedAt = DateTime.UtcNow;
        delivery.FailedReason = null;
        await db.SaveChangesAsync(ct);
        await realtime.OrderTimelineUpdatedAsync(delivery.Order.UserId, delivery.Order.Number, ct);
        return SaveResult.Success($"{rider.Name} assigned to {delivery.Order.Number}.");
    }

    public async Task<SaveResult> PickupAsync(Guid id, CancellationToken ct)
    {
        var delivery = await db.Deliveries.Include(d => d.Order).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (delivery is null) return SaveResult.Missing;
        if (delivery.Status != DeliveryStatuses.Assigned) return SaveResult.Error("Assign a rider before pickup.");
        delivery.Status = DeliveryStatuses.PickedUp;
        delivery.PickedUpAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await realtime.OrderTimelineUpdatedAsync(delivery.Order.UserId, delivery.Order.Number, ct);
        return SaveResult.Success("Pickup confirmed.");
    }

    /// Marking delivered closes the order. Pay-on-delivery riders hand in the order-total cash here.
    public async Task<SaveResult> DeliverAsync(Guid id, DeliverInput input, CancellationToken ct)
    {
        var delivery = await db.Deliveries.Include(d => d.Order).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (delivery is null) return SaveResult.Missing;
        if (delivery.Status == DeliveryStatuses.Delivered) return SaveResult.Done;
        if (delivery.RiderId is null) return SaveResult.Error("Assign a rider before marking delivered.");
        if (DeliveriesValidators.TextError("receivedBy", input.ReceivedBy, DeliveriesValidators.ReceivedByMax) is { } bad) return SaveResult.Invalid(bad.Field, bad.Message);

        var at = DateTime.UtcNow;
        var order = delivery.Order;
        delivery.Status = DeliveryStatuses.Delivered;
        delivery.DeliveredAt = at;
        delivery.ReceivedBy = string.IsNullOrWhiteSpace(input.ReceivedBy) ? null : input.ReceivedBy.Trim();

        if (order.PaymentMethod == PaymentMethods.PayOnDelivery)
        {
            delivery.CashCollected = input.Cash ?? order.Total;
            delivery.FeeCollected = order.ShippingFee;
            var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == order.Id && p.Status == PaymentStatuses.Pending, ct);
            if (payment is not null)
            {
                payment.Status = PaymentStatuses.Paid;
                payment.PaidAt = at;
                payment.ProviderTxRef = $"pod-cash-{delivery.Id.ToString("N")[..8]}";
            }
            // Pay-on-delivery orders never ship: confirm payment through the workflow, then close the order.
            if (order.Status == OrderStatuses.Pending)
                await orders.TransitionAsync(order, OrderStatuses.Paid, "Cash collected on delivery", null, ct);
            if (order.Status != OrderStatuses.Delivered)
            {
                order.Status = OrderStatuses.Delivered;
                order.DeliveredAt = at;
                order.UpdatedAt = at;
                db.OrderEvents.Add(new OrderEvent { OrderId = order.Id, Status = OrderStatuses.Delivered, Note = "Handed over by rider after cash payment", At = at });
            }
            await db.SaveChangesAsync(ct);
            await realtime.OrderChangedAsync(order, ct);
            await realtime.OrderTimelineUpdatedAsync(order.UserId, order.Number, ct);
        }
        else
        {
            // The transition saves the delivery in its own transaction, so neither half can land without the other.
            if (!await orders.TransitionAsync(order, OrderStatuses.Delivered, "Delivered by rider", null, ct))
                return SaveResult.Error($"Order {order.Number} is {order.Status} and cannot be marked delivered.");
        }
        return SaveResult.Success($"Delivery for {order.Number} completed.");
    }

    public async Task<SaveResult> FailAsync(Guid id, FailInput input, CancellationToken ct)
    {
        var delivery = await db.Deliveries.Include(d => d.Order).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (delivery is null) return SaveResult.Missing;
        if (delivery.Status == DeliveryStatuses.Delivered) return SaveResult.Done;
        if (string.IsNullOrWhiteSpace(input.Reason)) return SaveResult.Invalid("reason", "Say why the attempt failed.");
        if (DeliveriesValidators.TextError("reason", input.Reason, DeliveriesValidators.TextMax) is { } bad) return SaveResult.Invalid(bad.Field, bad.Message);
        if (delivery.Status == DeliveryStatuses.Failed) return SaveResult.Done;
        delivery.Status = DeliveryStatuses.Failed;
        delivery.FailedReason = input.Reason.Trim();
        delivery.Attempts++;
        await db.SaveChangesAsync(ct);
        await realtime.OrderTimelineUpdatedAsync(delivery.Order.UserId, delivery.Order.Number, ct);
        return SaveResult.Info("Delivery marked failed — reassign when ready.");
    }

    public async Task<SaveResult> NoteAsync(Guid id, NoteInput input, CancellationToken ct)
    {
        if (DeliveriesValidators.TextError("note", input.Note, DeliveriesValidators.TextMax) is { } bad) return SaveResult.Invalid(bad.Field, bad.Message);
        var note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        var delivery = await db.Deliveries.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (delivery is null) return SaveResult.Missing;
        delivery.Note = note;
        await db.SaveChangesAsync(ct);
        return SaveResult.Success(note is null ? "Note cleared." : "Note saved for the rider.");
    }

    // ---------------------------------------------------------------- riders

    public async Task<SaveResult> CreateRiderAsync(RiderInput input, CancellationToken ct)
    {
        if (input.Name?.Trim() is not { Length: >= 2 } name) return SaveResult.Invalid("name", "Rider name is required.");
        if (string.IsNullOrWhiteSpace(input.Phone)) return SaveResult.Invalid("phone", "Rider phone is required.");
        if (DeliveriesValidators.CapacityError(input.Capacity) is { } bad) return SaveResult.Invalid(bad.Field, bad.Message);
        db.Riders.Add(new Rider
        {
            Capacity = input.Capacity ?? RiderCapacity.Default,
            IsActive = input.IsActive ?? true,
            Name = name,
            Phone = input.Phone.Trim(),
            Vehicle = input.Vehicle?.Trim(),
            Service = RiderServiceNames.Contains(input.Service) ? input.Service! : RiderServices.InHouse,
        });
        await db.SaveChangesAsync(ct);
        return SaveResult.Success("Rider added.");
    }

    public async Task<SaveResult> UpdateRiderAsync(Guid id, RiderInput input, CancellationToken ct)
    {
        if (DeliveriesValidators.CapacityError(input.Capacity) is { } bad) return SaveResult.Invalid(bad.Field, bad.Message);
        var rider = await db.Riders.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rider is null) return SaveResult.Missing;
        if (input.Capacity is { } capacity) rider.Capacity = capacity;
        if (!string.IsNullOrWhiteSpace(input.Name)) rider.Name = input.Name.Trim();
        if (!string.IsNullOrWhiteSpace(input.Phone)) rider.Phone = input.Phone.Trim();
        if (input.Vehicle is not null) rider.Vehicle = input.Vehicle.Trim();
        if (input.Service is not null && RiderServiceNames.Contains(input.Service)) rider.Service = input.Service;
        if (input.IsActive is { } active) rider.IsActive = active;
        await db.SaveChangesAsync(ct);
        return SaveResult.Success("Rider updated.");
    }

    // ---------------------------------------------------------------- zones

    public async Task<SaveResult> CreateZoneAsync(ZoneInput input, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(input.Name)) errors["name"] = "Zone name is required.";
        if (input.Fee is not >= 0) errors["fee"] = "Fee must be zero or more.";
        if (string.IsNullOrWhiteSpace(input.EtaText)) errors["etaText"] = "Give customers an ETA (e.g. \"Same day if ordered by 2pm\").";
        if (errors.Count > 0) return new SaveResult(Errors: errors);
        db.DeliveryZones.Add(new DeliveryZone
        {
            IsActive = input.IsActive ?? true,
            Name = input.Name!.Trim(),
            Fee = input.Fee!.Value,
            EtaText = input.EtaText!.Trim(),
            AllowPayOnDelivery = input.AllowPayOnDelivery ?? false,
            SortOrder = (await db.DeliveryZones.MaxAsync(z => (int?)z.SortOrder, ct)) + 1 ?? 0,
        });
        await db.SaveChangesAsync(ct);
        return SaveResult.Success("Zone added.");
    }

    public async Task<SaveResult> UpdateZoneAsync(Guid id, ZoneInput input, CancellationToken ct)
    {
        var zone = await db.DeliveryZones.FirstOrDefaultAsync(z => z.Id == id, ct);
        if (zone is null) return SaveResult.Missing;
        if (!string.IsNullOrWhiteSpace(input.Name)) zone.Name = input.Name.Trim();
        if (input.Fee is >= 0) zone.Fee = input.Fee.Value;
        if (!string.IsNullOrWhiteSpace(input.EtaText)) zone.EtaText = input.EtaText.Trim();
        if (input.AllowPayOnDelivery is { } pod) zone.AllowPayOnDelivery = pod;
        if (input.IsActive is { } active) zone.IsActive = active;
        await db.SaveChangesAsync(ct);
        return SaveResult.Success("Zone updated.");
    }
}
