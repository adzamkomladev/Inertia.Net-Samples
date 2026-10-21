using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// Static status-machine guard: pending → paid → processing → shipped → delivered, cancel via != delivered.
public static class OrderWorkflow
{
    static readonly Dictionary<string, string[]> From = new()
    {
        [OrderStatuses.Pending] = [OrderStatuses.Paid, OrderStatuses.Cancelled],
        [OrderStatuses.Paid] = [OrderStatuses.Processing, OrderStatuses.Cancelled],
        [OrderStatuses.Processing] = [OrderStatuses.Shipped, OrderStatuses.Cancelled],
        [OrderStatuses.Shipped] = [OrderStatuses.Delivered],
        [OrderStatuses.Delivered] = [],
        [OrderStatuses.Cancelled] = [],
    };

    public static bool Can(string status, string to) => From.GetValueOrDefault(status, []).Contains(to);

    public static string CheckError(Order order, string to) =>
        $"Cannot move order {order.Number} from {order.Status} to {to}.";

    /// <summary>
    /// Applies the transition to a tracked order and writes the timeline event. The caller owns the
    /// transaction/update; side effects (payment state, delivery row, alerts, emails, SignalR) fire here.
    /// Restock on cancellation writes upside StockMovements so stock always equals SUM(movements).
    /// </summary>
    public static async Task<bool> MoveAsync(
        AppDb db,
        Order order,
        string to,
        string? note,
        Guid? byUserId,
        JobQueue jobs,
        RealtimeEvents realtime,
        CancellationToken ct = default)
    {
        if (!Can(order.Status, to) || order.Status == to) return false;
        var at = DateTime.UtcNow;
        order.Status = to;
        order.UpdatedAt = at;
        switch (to)
        {
            case OrderStatuses.Paid:
                order.PaidAt = at;
                order.PaymentStatus = PaymentStatuses.Paid;
                order.ReceiptNumber ??= OrderNumbers.Receipt(at);
                break;
            case OrderStatuses.Shipped:
                order.ShippedAt = at;
                if (!await db.Deliveries.AnyAsync(d => d.OrderId == order.Id, ct))
                    db.Deliveries.Add(new Delivery { OrderId = order.Id, ZoneId = order.ZoneId });
                break;
            case OrderStatuses.Delivered:
                order.DeliveredAt = at;
                break;
            case OrderStatuses.Cancelled:
                order.CancelledAt = at;
                order.CancelReason = note ?? order.CancelReason;
                if (order.PaymentStatus == PaymentStatuses.Paid)
                {
                    order.PaymentStatus = PaymentStatuses.Refunded; // dev policy: self-refunding cancel
                    jobs.Enqueue(db, new OrderRefundEmail(order.Id));
                }
                break;
        }

        var user = await db.Users.AsNoTracking().Where(u => u.Id == order.UserId)
            .Select(u => new { u.Id, u.Name, u.Role }).SingleAsync(ct);

        if (to == OrderStatuses.Cancelled)
        {
            // Release stock through movements so SUM(movements) stays the truth.
            var items = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == order.Id)
                .Select(i => new { i.Id, i.ProductId, i.Qty }).ToListAsync(ct);
            foreach (var item in items)
                if (item.ProductId != Guid.Empty)
                {
                    db.StockMovements.Add(new StockMovement
                    {
                        ProductId = item.ProductId, Delta = item.Qty, Reason = StockReasons.Release,
                        Note = $"Released: order {order.Number} cancelled", OrderId = order.Id, At = at,
                    });
                    await db.Products.IgnoreQueryFilters()
                        .Where(p => p.Id == item.ProductId)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + item.Qty), ct);
                }
        }

        db.OrderEvents.Add(new OrderEvent { OrderId = order.Id, Status = to, Note = note, ByUserId = byUserId, At = at });
        await db.SaveChangesAsync(ct);

        if (user.Role != "admin")
        {
            db.Alerts.Add(new Alert
            {
                UserId = order.UserId, Kind = AlertKinds.OrderStatus,
                Title = $"Order {order.Number} is {to}",
                Body = DescribeTransition(order, to, note),
                Href = $"/account/orders/{order.Number}",
                CreatedAt = at,
            });
        }
        if (to == OrderStatuses.Paid)
            jobs.Enqueue(db, new OrderPaidEmail(order.Id));
        if (to == OrderStatuses.Shipped)
            jobs.Enqueue(db, new OrderShippedEmail(order.Id));
        await db.SaveChangesAsync(ct);

        await realtime.OrderChangedAsync(order.Id, ct);
        await realtime.OrderTimelineUpdatedAsync(order.UserId, order.Number, ct);
        await realtime.NotifyUserAsync(order.UserId,
            $"Order {order.Number} is {to}",
            DescribeTransition(order, to, note),
            $"/account/orders/{order.Number}", ct);
        return true;
    }

    static string DescribeTransition(Order order, string to, string? note) => to switch
    {
        OrderStatuses.Paid => "Payment confirmed. We are preparing your order.",
        OrderStatuses.Processing => "Your order is being packed.",
        OrderStatuses.Shipped => $"Shipped. {note}",
        OrderStatuses.Delivered => "Delivered. Enjoy your new tech!",
        OrderStatuses.Cancelled => $"Order cancelled. {note ?? "Payment refunded where applicable."}",
        _ => note ?? "",
    };
}
