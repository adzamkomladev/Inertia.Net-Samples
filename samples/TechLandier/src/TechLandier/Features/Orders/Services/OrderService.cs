using static TechLandier.Features.OrderUtils;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// Order reads for customers (guest tracking, account) and admins, the status machine, and human numbering.
/// The detail loaders run as parallel Inertia props, so they use their own contexts.
public sealed class OrderService(AppDb db, IDbContextFactory<AppDb> dbs, JobQueue jobs, RealtimeEvents realtime)
{
    const int AccountPageSize = 50;
    const int AdminPageSize = 25;

    static readonly Dictionary<string, string[]> Transitions = new()
    {
        [OrderStatuses.Pending] = [OrderStatuses.Paid, OrderStatuses.Cancelled],
        [OrderStatuses.Paid] = [OrderStatuses.Processing, OrderStatuses.Cancelled],
        [OrderStatuses.Processing] = [OrderStatuses.Shipped, OrderStatuses.Cancelled],
        [OrderStatuses.Shipped] = [OrderStatuses.Delivered],
    };

    static readonly string[] AdminFilters =
        [OrderStatuses.Pending, OrderStatuses.Paid, OrderStatuses.Processing, OrderStatuses.Shipped, OrderStatuses.Delivered, OrderStatuses.Cancelled];

    public static bool CanMove(string from, string to) => Transitions.GetValueOrDefault(from)?.Contains(to) == true;

    // ----------------------------------------------------------- order detail

    static IQueryable<OrderDetail> Details(IQueryable<Order> orders) =>
        orders.Select(o => new OrderDetail(
            o.Id, o.Number, o.Status, o.PaymentStatus, o.PaymentMethod, o.Subtotal, o.DiscountTotal, o.ShippingFee, o.Total,
            o.PaidAt, o.ShippedAt, o.DeliveredAt, o.CancelledAt, o.CancelReason, o.ReceiptNumber, o.CreatedAt,
            o.Items.Select(i => new OrderItemRow(i.Id, i.ProductId, i.Name, i.Sku, i.ImageArt, i.UnitPrice, i.Qty, i.LineTotal, i.Product != null ? i.Product.Slug : null)).ToList()));

    /// Guests follow /orders/{number}?t={token}; owners open it signed in. Guests with a wrong token learn nothing.
    public async Task<(OrderDetail? Order, bool Forbidden)> TrackedOrderAsync(string number, string? token, Guid? uid, CancellationToken ct)
    {
        var found = await db.Orders.AsNoTracking().IgnoreQueryFilters()
            .Where(o => o.Number == number)
            .Select(o => new { o.UserId, o.AccessToken })
            .FirstOrDefaultAsync(ct);
        if (found is null || (found.UserId != uid && found.AccessToken != token))
            return (null, found is not null && uid is not null);
        return (await Details(db.Orders.AsNoTracking().IgnoreQueryFilters().Where(o => o.Number == number)).FirstAsync(ct), false);
    }

    public async Task<OrderDetail?> AccountOrderAsync(string number, Guid uid, CancellationToken ct) =>
        await Details(db.Orders.AsNoTracking().IgnoreQueryFilters().Where(o => o.Number == number && o.UserId == uid)).FirstOrDefaultAsync(ct);

    public async Task<OrderDetail?> AdminOrderAsync(string number, CancellationToken ct) =>
        await Details(db.Orders.AsNoTracking().IgnoreQueryFilters().Where(o => o.Number == number)).FirstOrDefaultAsync(ct);

    public async Task<List<OrderEventRow>> EventsAsync(Guid orderId, CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await own.OrderEvents.AsNoTracking()
            .Where(e => e.OrderId == orderId)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => new OrderEventRow(e.Id, e.Status, e.Note, e.At))
            .ToListAsync(ct);
    }

    public async Task<DeliveryView?> DeliveryAsync(Guid orderId, CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await own.Deliveries.AsNoTracking()
            .Where(d => d.OrderId == orderId)
            .Select(d => new DeliveryView(
                d.Status, d.FeeCollected, d.CashCollected, d.FailedReason, d.AssignedAt, d.PickedUpAt, d.DeliveredAt,
                d.Rider != null ? d.Rider.Name : null, d.Rider != null ? d.Rider.Phone : null, d.Rider != null ? d.Rider.Vehicle : null,
                d.Rider != null ? d.Rider.Service : null, d.Zone != null ? d.Zone.Name : null, d.ZoneId, d.Zone != null ? d.Zone.EtaText : null))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PlanView?> PlanAsync(Guid orderId, CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await CreditService.PlanViews(own, own.PayPlans.Where(p => p.OrderId == orderId)).FirstOrDefaultAsync(ct);
    }

    public async Task<List<WarrantyView>> WarrantiesAsync(Guid orderId, CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await own.Warranties.AsNoTracking()
            .Where(w => w.OrderItem.OrderId == orderId)
            .Select(w => new WarrantyView(w.Id, w.OrderItem.Name, w.SerialNumber, w.StartOn, w.EndOn, w.Status))
            .ToListAsync(ct);
    }

    public async Task<List<InsuranceView>> InsurancesAsync(Guid orderId, CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await own.DeviceInsurances.AsNoTracking()
            .Where(d => d.OrderId == orderId)
            .Select(d => new InsuranceView(d.Id, d.ProductId, d.DeviceName, d.DeviceSerial, d.Premium, d.CoverageAmount, d.StartOn, d.EndOn, d.Status))
            .ToListAsync(ct);
    }

    /// Items of the order the customer can still review (active product, no review yet).
    public async Task<List<ReviewCta>> ReviewCtasAsync(Guid orderId, Guid uid, CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await own.OrderItems.AsNoTracking()
            .Where(i => i.OrderId == orderId && i.Product != null && !own.Reviews.Any(r => r.ProductId == i.ProductId && r.UserId == uid))
            .Select(i => new ReviewCta(i.Product!.Slug, i.Name, i.Order.DeliveredAt != null))
            .ToListAsync(ct);
    }

    public async Task<List<PaymentRow>> PaymentsAsync(Guid orderId, CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await own.Payments.AsNoTracking()
            .Where(p => p.OrderId == orderId)
            .Select(p => new PaymentRow(p.Id, p.Method, p.ProviderTxRef, p.Amount, p.Status, p.PaidAt))
            .ToListAsync(ct);
    }

    public async Task<OrderCustomer> CustomerAsync(Guid orderId, CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await own.Orders.AsNoTracking().Where(o => o.Id == orderId)
            .Select(o => new OrderCustomer(o.AddressSnapshot.RecipientName, o.AddressSnapshot.Phone, o.AddressSnapshot.StreetLabel, o.Notes, o.User.Email))
            .FirstAsync(ct);
    }

    public async Task<List<RiderRow>> ActiveRidersAsync(CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await own.Riders.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Name)
            .Select(r => new RiderRow(r.Id, r.Name, r.Phone, r.Vehicle, r.Service))
            .ToListAsync(ct);
    }

    // --------------------------------------------------------------- lists

    public async Task<List<AccountOrderRow>> AccountOrdersAsync(Guid uid, CancellationToken ct) =>
        await db.Orders.AsNoTracking()
            .Where(o => o.UserId == uid)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(AccountPageSize)
            .Select(o => new AccountOrderRow(
                o.Id, o.Number, o.Status, o.PaymentStatus, o.PaymentMethod, o.Total, o.CreatedAt,
                o.Items.Select(i => i.Name).ToList(),
                db.Reviews.Any(r => r.UserId == uid && o.Items.Any(i => i.ProductId == r.ProductId))))
            .ToListAsync(ct);

    public async Task<Paged<AdminOrderRow>> AdminOrdersAsync(string? status, string? q, int? page, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking();
        if (status is not null && AdminFilters.Contains(status)) query = query.Where(o => o.Status == status);
        if (q?.Trim() is { Length: > 0 } term)
        {
            var like = $"%{term}%";
            query = query.Where(o => EF.Functions.ILike(o.Number, like)
                || EF.Functions.ILike(o.AddressSnapshot.RecipientName, like)
                || EF.Functions.ILike(o.User.Email, like));
        }
        return await query
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Select(o => new AdminOrderRow(
                o.Id, o.Number, o.Status, o.PaymentStatus, o.PaymentMethod, o.Total, o.CreatedAt,
                o.AddressSnapshot.RecipientName, o.User.Email,
                db.Deliveries.Where(d => d.OrderId == o.Id).Select(d => new DeliveryBrief(d.Status, d.Rider != null ? d.Rider.Name : null)).FirstOrDefault()))
            .ToPagedAsync(page, AdminPageSize, ct);
    }

    // ------------------------------------------------- state machine engine

    /// Applies a status change to a tracked order with its side effects: timeline event, payment state, delivery row,
    /// stock release on cancel, alert, emails and SignalR pushes. Runs in the caller's transaction when there is one.
    /// Returns false when the workflow does not allow the move.
    public async Task<bool> TransitionAsync(Order order, string to, string? note, Guid? byUserId, CancellationToken ct)
    {
        if (!CanMove(order.Status, to)) return false;
        var at = DateTime.UtcNow;
        order.Status = to;
        order.UpdatedAt = at;
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;

        switch (to)
        {
            case OrderStatuses.Paid:
                order.PaidAt = at;
                order.PaymentStatus = PaymentStatuses.Paid;
                order.ReceiptNumber ??= NewReceiptNumber(at);
                jobs.Enqueue(db, new OrderPaidEmail(order.Id));
                break;
            case OrderStatuses.Shipped:
                order.ShippedAt = at;
                if (!await db.Deliveries.AnyAsync(d => d.OrderId == order.Id, ct))
                    db.Deliveries.Add(new Delivery { OrderId = order.Id, ZoneId = order.ZoneId });
                jobs.Enqueue(db, new OrderShippedEmail(order.Id));
                break;
            case OrderStatuses.Delivered:
                order.DeliveredAt = at;
                break;
            case OrderStatuses.Cancelled:
                order.CancelledAt = at;
                order.CancelReason = note ?? order.CancelReason;
                if (order.PaymentStatus == PaymentStatuses.Paid)
                {
                    order.PaymentStatus = PaymentStatuses.Refunded; // dev policy: a cancelled paid order refunds itself
                    jobs.Enqueue(db, new OrderRefundEmail(order.Id));
                }
                await ReleaseStockAsync(order, at, ct);
                break;
        }

        db.OrderEvents.Add(new OrderEvent { OrderId = order.Id, Status = to, Note = note, ByUserId = byUserId, At = at });
        var title = $"Order {order.Number} is {to}";
        var body = to switch
        {
            OrderStatuses.Paid => "Payment confirmed. We are preparing your order.",
            OrderStatuses.Processing => "Your order is being packed.",
            OrderStatuses.Shipped => $"Shipped. {note}",
            OrderStatuses.Delivered => "Delivered. Enjoy your new tech!",
            _ => $"Order cancelled. {note ?? "Payment refunded where applicable."}",
        };
        var href = $"/account/orders/{order.Number}";
        if (await db.Users.Where(u => u.Id == order.UserId).Select(u => u.Role).FirstAsync(ct) != "admin")
            db.Alerts.Add(new Alert { UserId = order.UserId, Kind = AlertKinds.OrderStatus, Title = title, Body = body, Href = href, CreatedAt = at });
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        jobs.Wake();

        await realtime.OrderChangedAsync(order, ct);
        await realtime.OrderTimelineUpdatedAsync(order.UserId, order.Number, ct);
        await realtime.NotifyUserAsync(order.UserId, title, body, href, ct);
        return true;
    }

    /// Cancelling returns the reserved stock; the ledger gets a matching upside movement per item.
    async Task ReleaseStockAsync(Order order, DateTime at, CancellationToken ct)
    {
        var items = await db.OrderItems.AsNoTracking()
            .Where(i => i.OrderId == order.Id && i.ProductId != Guid.Empty)
            .Select(i => new { i.ProductId, i.Qty })
            .ToListAsync(ct);
        foreach (var item in items)
        {
            db.StockMovements.Add(new StockMovement
            {
                ProductId = item.ProductId, Delta = item.Qty, Reason = StockReasons.Release,
                Note = $"Released: order {order.Number} cancelled", OrderId = order.Id, At = at,
            });
            await db.Products.IgnoreQueryFilters().Where(p => p.Id == item.ProductId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + item.Qty), ct);
        }
    }

    // ------------------------------------------------------------ numbering

    /// Next order number TL-2026-00001. The advisory lock is transaction scoped, so call this inside the owning transaction.
    public async Task<string> NextOrderNumberAsync(CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext('techlandier-order-number'))", ct);
        var prefix = $"TL-{DateTime.UtcNow.Year}-";
        var last = await db.Orders.IgnoreQueryFilters().Where(o => o.Number.StartsWith(prefix))
            .OrderByDescending(o => o.Number).Select(o => o.Number).FirstOrDefaultAsync(ct);
        return $"{prefix}{(last is null ? 1 : int.Parse(last[prefix.Length..]) + 1):D5}";
    }

    // ------------------------------------------------------------ admin actions

    public async Task<SaveResult> MoveAsync(string number, MoveOrderInput input, Guid adminId, CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Number == number, ct);
        if (order is null) return SaveResult.Missing;
        if (input.To is null || !await TransitionAsync(order, input.To, input.Note, adminId, ct))
            return SaveResult.Error($"Cannot move order {number} from {order.Status} to {input.To ?? "nothing"}.");
        return SaveResult.Success($"Order {number} moved to {input.To}.");
    }

    public async Task<SaveResult> RefundAsync(string number, RefundInput input, Guid adminId, CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Number == number, ct);
        if (order is null) return SaveResult.Missing;
        if (order.PaymentStatus != PaymentStatuses.Paid || order.Status == OrderStatuses.Cancelled)
            return SaveResult.Error("Only paid, open orders can be refunded.");
        return await TransitionAsync(order, OrderStatuses.Cancelled, input.Note ?? "Refund issued by support", adminId, ct)
            ? SaveResult.Success($"Order {number} refunded.")
            : SaveResult.Error("Refund failed.");
    }

    public async Task<SaveResult> SettlePaymentAsync(Guid id, Guid adminId, CancellationToken ct)
    {
        var payment = await db.Payments.Include(p => p.Order).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payment is null) return SaveResult.Missing;
        if (payment.Status == PaymentStatuses.Paid) return SaveResult.Info("That payment is already settled.");
        if (payment.Status == PaymentStatuses.Refunded) return SaveResult.Error("That payment was refunded.");
        payment.Status = PaymentStatuses.Paid;
        payment.PaidAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (payment.Order.Status == OrderStatuses.Pending)
            await TransitionAsync(payment.Order, OrderStatuses.Paid, "Payment verified by admin", adminId, ct);
        return SaveResult.Success("Payment settled.");
    }
}
