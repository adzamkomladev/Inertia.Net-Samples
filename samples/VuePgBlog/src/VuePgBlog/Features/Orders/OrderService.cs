using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Infrastructure;

namespace VuePgBlog.Features;

/// Order reads for customers (guest tracking + account) plus the admin order actions.
public static partial class OrderService
{
    // ----------------------------------------------------- public tracking

    /// Guests follow /orders/{number}?t={token}; owners load it signed-in without a token.
    internal static async Task<IResult> TrackAsync(string number, string? t, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.Number == number)
            .Select(o => new
            {
                o.Id,
                o.Number,
                o.Status,
                o.PaymentStatus,
                o.PaymentMethod,
                o.Total,
                o.AccessToken,
                o.UserId,
                o.PaidAt,
                o.ShippedAt,
                o.DeliveredAt,
                o.CancelledAt,
            })
            .FirstOrDefaultAsync(ct);
        if (order is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        var uid = ctx.User.UserIdOrNull();
        var owner = uid is { } id && id == order.UserId;
        if (!owner && t != order.AccessToken)
        {
            if (uid is not null) return HttpExtensions.Forbidden(); // signed in, not the owner
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404); // guests: no leak
        }
        return Render(
            "orders/Track",
            new InertiaProps
            {
                ["token"] = t,
                ["order"] = await OrderView(db, order.Id, ct),
                ["events"] = await TransitionEvents(db, order.Id, ct),
                ["delivery"] = await DeliveryView(db, order.Id, ct),
            }
        );
    }

    internal static async Task<object?> OrderView(AppDb db, Guid orderId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new
            {
                o.Id,
                o.Number,
                o.Status,
                o.PaymentStatus,
                o.PaymentMethod,
                o.Subtotal,
                o.DiscountTotal,
                o.ShippingFee,
                o.Total,
                o.PaidAt,
                o.ShippedAt,
                o.DeliveredAt,
                o.CancelledAt,
                o.CancelReason,
                o.ReceiptNumber,
                o.CreatedAt,
                Items = o.Items.Select(i => new OrderItemRow(
                    i.Id,
                    i.ProductId,
                    i.Name,
                    i.Sku,
                    i.ImageArt,
                    i.UnitPrice,
                    i.Qty,
                    i.LineTotal,
                    db.Products.IgnoreQueryFilters().Where(p => p.Id == i.ProductId).Select(p => p.Slug).FirstOrDefault()
                )).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        return order;
    }

    internal static async Task<List<OrderEventRow>> TransitionEvents(AppDb db, Guid orderId, CancellationToken ct) =>
        (await db.OrderEvents.AsNoTracking()
            .Where(e => e.OrderId == orderId)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => new { e.Id, e.Status, e.Note, e.At })
            .ToListAsync(ct))
            .Select(r => new OrderEventRow(r.Id, r.Status, r.Note, r.At))
            .ToList();

    internal static async Task<object?> DeliveryView(AppDb db, Guid orderId, CancellationToken ct) =>
        await db.Deliveries.AsNoTracking()
            .Where(d => d.OrderId == orderId)
            .Select(d => new
            {
                d.Status,
                d.FeeCollected,
                d.CashCollected,
                d.FailedReason,
                d.AssignedAt,
                d.PickedUpAt,
                d.DeliveredAt,
                RiderName = d.Rider != null ? d.Rider.Name : null,
                RiderPhone = d.Rider != null ? d.Rider.Phone : null,
                Vehicle = d.Rider != null ? d.Rider.Vehicle : null,
                Service = d.Rider != null ? d.Rider.Service : null,
                ZoneName = d.Zone != null ? d.Zone.Name : null,
                d.ZoneId,
                ZoneEta = d.Zone != null ? d.Zone.EtaText : null,
            })
            .FirstOrDefaultAsync(ct);

    // --------------------------------------------------------- account pages

    internal static async Task<IResult> AccountOrdersAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var uid = ctx.User.UserId();
        var rows = await db.Orders.AsNoTracking()
            .Where(o => o.UserId == uid)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(50)
            .Select(o => new
            {
                o.Id,
                o.Number,
                o.Status,
                o.PaymentStatus,
                o.PaymentMethod,
                o.Total,
                o.CreatedAt,
                ItemNames = o.Items.Select(i => i.Name).ToList(),
                HasReview = db.Reviews.Any(r => r.UserId == uid
                    && o.Items.Select(i => i.ProductId).Contains(r.ProductId)),
            })
            .ToListAsync(ct);
        return Render(
            "account/Orders",
            new InertiaProps { ["orders"] = rows.Select(r => new { r.Id, r.Number, r.Status, r.PaymentStatus, r.PaymentMethod, r.Total, r.CreatedAt, r.ItemNames, r.HasReview }).ToList() }
        );
    }

    internal static async Task<IResult> AccountOrderAsync(string number, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var uid = ctx.User.UserId();
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.Number == number && o.UserId == uid)
            .Select(o => new { o.Id })
            .FirstOrDefaultAsync(ct);
        if (order is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        return Render(
            "account/Order",
            new InertiaProps
            {
                ["order"] = await OrderView(db, order.Id, ct),
                ["events"] = await TransitionEvents(db, order.Id, ct),
                ["delivery"] = await DeliveryView(db, order.Id, ct),
                ["plan"] = await db.PayPlans.AsNoTracking()
                    .Where(p => p.OrderId == order.Id)
                    .Select(p => new PlanView(
                        p.Id,
                        p.InstallmentCount,
                        p.AmountPerInstallment,
                        p.FeeTotal,
                        p.Status,
                        p.StartedAt,
                        p.CompletedAt,
                        db.Installments.Where(i => i.PlanId == p.Id).OrderBy(i => i.Number)
                            .Select(i => new InstallmentRow(i.Id, i.Number, i.Amount, i.DueDate, i.Status, i.PaidAt)).ToList()))
                    .FirstOrDefaultAsync(ct),
                ["warranties"] = await db.Warranties.AsNoTracking()
                    .Where(w => w.OrderItem.OrderId == order.Id)
                    .Select(w => new WarrantyView(w.Id, w.OrderItem.Name, w.SerialNumber, w.StartOn, w.EndOn, w.Status))
                    .ToListAsync(ct),
                ["insurances"] = await db.DeviceInsurances.AsNoTracking()
                    .Where(d => d.OrderId == order.Id)
                    .Select(d => new InsuranceView(d.Id, d.ProductId, d.DeviceName, d.DeviceSerial, d.Premium, d.CoverageAmount, d.StartOn, d.EndOn, d.Status))
                    .ToListAsync(ct),
                ["reviewSlugs"] = await db.OrderItems.AsNoTracking()
                    .Where(i => i.OrderId == order.Id && i.ProductId != Guid.Empty
                        && !db.Reviews.Any(r => r.ProductId == i.ProductId && r.UserId == uid))
                    .Select(i => new ReviewCta(i.Product.Id != Guid.Empty ? i.Product.Slug : "", i.Name, i.Order.DeliveredAt != null))
                    .ToListAsync(ct),
            }
        );
    }

    // ------------------------------------------------------------ admin

    internal static async Task<IResult> AdminOrdersAsync(string? status, string? q, int? page, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var pageNo = Math.Max(page ?? 1, 1);
        var size = 25;
        var query = db.Orders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) && AdminStatuses.Contains(status))
            query = query.Where(o => o.Status == status);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(o => EF.Functions.ILike(o.Number, pattern)
                || EF.Functions.ILike(o.AddressSnapshot.RecipientName, pattern)
                || EF.Functions.ILike(o.User.Email, pattern));
        }
        var rows = await query
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Skip((pageNo - 1) * size).Take(size + 1)
            .Select(o => new
            {
                o.Id,
                o.Number,
                o.Status,
                o.PaymentStatus,
                o.PaymentMethod,
                o.Total,
                o.CreatedAt,
                CustomerName = o.AddressSnapshot.RecipientName,
                CustomerEmail = o.User.Email,
                Delivery = db.Deliveries.Where(d => d.OrderId == o.Id)
                    .Select(d => new { d.Status, RiderName = d.Rider != null ? d.Rider.Name : null })
                    .FirstOrDefault(),
                Unassigned = db.Deliveries.Any(d => d.OrderId == o.Id && d.Status == DeliveryStatuses.Unassigned),
            })
            .ToListAsync(ct);
        return Render(
            "admin/Orders",
            new InertiaProps
            {
                ["orders"] = new AdminOrdersPage(
                    rows.Take(size).Select(r => (object)new { r.Id, r.Number, r.Status, r.PaymentStatus, r.PaymentMethod, r.Total, r.CreatedAt, r.CustomerName, r.CustomerEmail, r.Delivery, r.Unassigned }).ToList(),
                    pageNo,
                    rows.Count > size),
                ["statusFilter"] = status ?? "",
                ["q"] = q ?? "",
            }
        );
    }

    static readonly string[] AdminStatuses =
        [OrderStatuses.Pending, OrderStatuses.Paid, OrderStatuses.Processing, OrderStatuses.Shipped, OrderStatuses.Delivered, OrderStatuses.Cancelled];

    internal static async Task<IResult> AdminOrderAsync(string number, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.Number == number)
            .Select(o => new { o.Id })
            .FirstOrDefaultAsync(ct);
        if (order is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        return Render(
            "admin/Order",
            new InertiaProps
            {
                ["order"] = await OrderView(db, order.Id, ct),
                ["events"] = await TransitionEvents(db, order.Id, ct),
                ["delivery"] = await DeliveryView(db, order.Id, ct),
                ["customer"] = await db.Orders.AsNoTracking().Where(o => o.Id == order.Id)
                    .Select(o => new
                    {
                        Name = o.AddressSnapshot.RecipientName,
                        Phone = o.AddressSnapshot.Phone,
                        Street = o.AddressSnapshot.StreetLabel,
                        o.Notes,
                        Email = o.User.Email,
                    })
                    .FirstAsync(ct),
                ["payments"] = await db.Payments.AsNoTracking()
                    .Where(p => p.OrderId == order.Id)
                    .Select(p => new PaymentRow(p.Id, p.Method, p.ProviderTxRef, p.Amount, p.Status, p.PaidAt))
                    .ToListAsync(ct),
                ["plan"] = await db.PayPlans.AsNoTracking()
                    .Where(p => p.OrderId == order.Id)
                    .Select(p => new PlanView(
                        p.Id,
                        p.InstallmentCount,
                        p.AmountPerInstallment,
                        p.FeeTotal,
                        p.Status,
                        p.StartedAt,
                        p.CompletedAt,
                        db.Installments.Where(i => i.PlanId == p.Id).OrderBy(i => i.Number)
                            .Select(i => new InstallmentRow(i.Id, i.Number, i.Amount, i.DueDate, i.Status, i.PaidAt)).ToList()))
                    .FirstOrDefaultAsync(ct),
                ["activeRiders"] = await db.Riders.AsNoTracking()
                    .Where(r => r.IsActive)
                    .OrderBy(r => r.Name)
                    .Select(r => new RiderRow(r.Id, r.Name, r.Phone, r.Vehicle, r.Service))
                    .ToListAsync(ct),
            }
        );
    }

    internal static async Task<IResult> MoveAsync(string number, string? to, string? note, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var adminId = ctx.User.UserId();
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Number == number, ct);
        if (order is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (to is null || !OrderWorkflow.Can(order.Status, to) || to == order.Status)
            return Back().WithFlash("toast", new Toast("error", OrderWorkflow.CheckError(order, to ?? "?" + to)));
        if (!await OrderWorkflow.MoveAsync(db, order, to, note, adminId, jobs, realtime, ct))
            return Back().WithFlash("toast", new Toast("error", OrderWorkflow.CheckError(order, to)));
        ctx.Inertia().Flash("toast", new Toast("success", $"Order {number} moved to {to}."));
        return Back();
    }

    internal static async Task<IResult> RefundAsync(string number, string? note, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var adminId = ctx.User.UserId();
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Number == number, ct);
        if (order is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (order.PaymentStatus != PaymentStatuses.Paid || order.Status == OrderStatuses.Cancelled)
            return Back().WithFlash("toast", new Toast("error", "Only paid, open orders can be refunded."));
        var ok = await OrderWorkflow.MoveAsync(db, order, OrderStatuses.Cancelled, note ?? "Refund issued by support", adminId, jobs, realtime, ct);
        ctx.Inertia().Flash("toast", new Toast(ok ? "success" : "error", ok ? $"Order {number} refunded." : "Refund failed."));
        return Back();
    }

    internal static async Task<IResult> SettlePaymentAsync(Guid id, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var adminId = ctx.User.UserId();
        var payment = await db.Payments.Include(p => p.Order).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payment is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (payment.Status == PaymentStatuses.Paid)
            return Back().WithFlash("toast", new Toast("info", "That payment is already settled."));
        if (payment.Status == PaymentStatuses.Refunded)
            return Back().WithFlash("toast", new Toast("error", "That payment was refunded."));
        payment.Status = PaymentStatuses.Paid;
        payment.PaidAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (payment.Order.Status == OrderStatuses.Pending)
            await OrderWorkflow.MoveAsync(db, payment.Order, OrderStatuses.Paid, "Payment verified by admin", adminId, jobs, realtime, ct);
        ctx.Inertia().Flash("toast", new Toast("success", "Payment settled."));
        return Back();
    }
}

public sealed record OrderItemRow(Guid Id, Guid ProductId, string Name, string Sku, string ImageArt, decimal UnitPrice, int Qty, decimal LineTotal, string? Slug);
public sealed record OrderEventRow(Guid Id, string Status, string? Note, DateTime At);
public sealed record PlanView(Guid Id, int InstallmentCount, decimal AmountPerInstallment, decimal FeeTotal, string Status, DateTime StartedAt, DateTime? CompletedAt, List<InstallmentRow> Installments);
public sealed record InstallmentRow(Guid Id, int Number, decimal Amount, DateOnly DueDate, string Status, DateTime? PaidAt);
public sealed record WarrantyView(Guid Id, string ItemName, string? SerialNumber, DateOnly StartOn, DateOnly EndOn, string Status);
public sealed record InsuranceView(Guid Id, Guid? ProductId, string DeviceName, string? DeviceSerial, decimal Premium, decimal CoverageAmount, DateOnly StartOn, DateOnly EndOn, string Status);
public sealed record ReviewCta(string Slug, string Name, bool Eligible);
public sealed record PaymentRow(Guid Id, string Method, string? ProviderTxRef, decimal Amount, string Status, DateTime? PaidAt);
public sealed record RiderRow(Guid Id, string Name, string Phone, string? Vehicle, string Service);
public sealed record ZoneRowInput(string Name, decimal Fee, string EtaText, bool AllowPayOnDelivery);
public sealed record AdminOrdersPage(IReadOnlyList<object> Data, int Page, bool HasMore) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}
