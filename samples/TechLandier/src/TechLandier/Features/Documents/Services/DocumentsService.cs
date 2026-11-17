using Microsoft.EntityFrameworkCore;
using TechLandier.Data;

namespace TechLandier.Features;

/// Reads the data behind receipts and statements. Authorization lives in the queries: a caller who may not see a
/// document gets null, indistinguishable from a document that does not exist.
public sealed class DocumentsService(AppDb db)
{
    /// Who is asking for an order receipt: the owner, a guest holding the order's access token, or an admin.
    public sealed record Requester(Guid? UserId, string? Token = null, bool Admin = false);

    /// Receipts exist for paid orders only; a cancelled (refunded) or unpaid order has none.
    public async Task<ReceiptData?> ReceiptAsync(string number, Requester who, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().IgnoreQueryFilters()
            .Where(o => o.Number == number
                && (who.Admin || (who.UserId != null && o.UserId == who.UserId) || (who.Token != null && o.AccessToken == who.Token)))
            .Select(o => o.Id).FirstOrDefaultAsync(ct);
        return order == Guid.Empty ? null : await ReceiptByIdAsync(order, ct);
    }

    public async Task<ReceiptData?> ReceiptByIdAsync(Guid orderId, CancellationToken ct)
    {
        var o = await db.Orders.AsNoTracking().IgnoreQueryFilters()
            .Where(o => o.Id == orderId && o.PaymentStatus == PaymentStatuses.Paid && o.ReceiptNumber != null && o.PaidAt != null)
            .Select(o => new
            {
                o.Number, o.ReceiptNumber, o.CreatedAt, o.PaidAt, o.PaymentMethod, o.Subtotal, o.DiscountTotal, o.ShippingFee, o.Total,
                CustomerName = o.User.Name, CustomerEmail = o.User.Email, o.AddressSnapshot,
                Lines = o.Items.OrderBy(i => i.Name).Select(i => new ReceiptLine(i.Name, i.Sku, i.Qty, i.UnitPrice, i.LineTotal)).ToList(),
                PaymentRef = db.Payments.Where(p => p.OrderId == o.Id && p.Status == PaymentStatuses.Paid)
                    .OrderByDescending(p => p.PaidAt).Select(p => p.ProviderTxRef).FirstOrDefault(),
                Plan = db.PayPlans.Where(p => p.OrderId == o.Id).Select(p => new
                {
                    p.InstallmentCount, p.AmountPerInstallment, p.FeeTotal,
                    FirstDue = db.Installments.Where(i => i.PlanId == p.Id).Min(i => (DateOnly?)i.DueDate),
                }).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);
        if (o is null) return null;
        var store = await StoreInfo.LoadAsync(db, ct);
        return new ReceiptData(store, o.Number, o.ReceiptNumber!, o.CreatedAt, o.PaidAt!.Value, o.CustomerName, o.CustomerEmail,
            o.AddressSnapshot.RecipientName, o.AddressSnapshot.Phone, o.AddressSnapshot.StreetLabel, o.Lines,
            o.Subtotal, o.DiscountTotal, o.ShippingFee, o.Total, o.PaymentMethod, o.PaymentRef,
            o.Plan is { } p ? new ReceiptPlan(p.InstallmentCount, p.AmountPerInstallment, p.FeeTotal, p.FirstDue ?? DateOnly.FromDateTime(o.CreatedAt)) : null);
    }

    /// Statements belong to the plan's owner only.
    public async Task<StatementData?> StatementAsync(Guid planId, Guid userId, CancellationToken ct) =>
        await StatementCoreAsync(db.PayPlans.Where(p => p.Id == planId && p.UserId == userId), ct);

    public async Task<StatementData?> StatementForEmailAsync(Guid planId, CancellationToken ct) =>
        await StatementCoreAsync(db.PayPlans.Where(p => p.Id == planId), ct);

    async Task<StatementData?> StatementCoreAsync(IQueryable<PayPlan> plans, CancellationToken ct)
    {
        var p = await plans.AsNoTracking().Select(p => new
        {
            p.Id, OrderNumber = p.Order.Number, p.Status, p.StartedAt, p.CompletedAt, p.InstallmentCount, p.AmountPerInstallment, p.FeeTotal,
            CustomerName = p.User.Name, CustomerEmail = p.User.Email,
            Rows = db.Installments.Where(i => i.PlanId == p.Id).OrderBy(i => i.Number)
                .Select(i => new StatementRow(i.Number, i.Amount, i.DueDate, i.Status, i.PaidAt)).ToList(),
        }).FirstOrDefaultAsync(ct);
        if (p is null) return null;
        var store = await StoreInfo.LoadAsync(db, ct);
        return new StatementData(store, p.Id, p.OrderNumber, p.CustomerName, p.CustomerEmail, p.Status, p.StartedAt, p.CompletedAt,
            p.InstallmentCount, p.AmountPerInstallment, p.FeeTotal, p.Rows);
    }
}
