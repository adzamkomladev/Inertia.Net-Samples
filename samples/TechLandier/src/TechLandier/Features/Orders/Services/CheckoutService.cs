using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;
using Inertia.Net;

namespace TechLandier.Features;

/// Idempotent checkout. One product per order (there is no cart); stock is reserved with a conditional
/// UPDATE ... WHERE Stock >= qty inside the transaction, so overselling races lose cleanly.
public sealed class CheckoutService(
    AppDb db, OrderService orders, CreditService credit, RealtimeEvents realtime, StockAlerts alerts, IHostEnvironment env,
    IInertiaCache cache)
{
    const int MaxActivePlans = 2;

    public async Task<SaveResult> CreateAsync(CheckoutInput input, Guid uid, CancellationToken ct)
    {
        if (input.PaymentMethod is not (PaymentMethods.Card or PaymentMethods.MobileMoney or PaymentMethods.PayOnDelivery) || input.ProductId is not { } productId)
            return SaveResult.Error("Choose a payment method.");
        if (input.BnplCount is not (null or 3 or 6 or 12))
            return SaveResult.Error("Installments can only be 3, 6 or 12.");
        var method = input.PaymentMethod;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Per-user advisory lock: serializes the plan-limit checks and makes an idempotent retry safe.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({uid.ToString()}))", ct);

        var existing = await db.Orders.AsNoTracking()
            .Where(o => o.UserId == uid && o.CheckoutAttemptId == input.CheckoutAttemptId)
            .Select(o => new { o.Number, o.AccessToken })
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return SaveResult.Info("This order was already placed.", $"/orders/{existing.Number}?t={existing.AccessToken}");

        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Id, p.Name, p.Brand, p.Price, p.DiscountPercent, p.Stock, p.LowStockThreshold, p.WarrantyMonths, p.IsInsurable, CategoryIcon = p.Category.Icon })
            .FirstOrDefaultAsync(ct);
        if (product is null) return SaveResult.Error("That product is no longer available.");
        var qty = Math.Clamp(input.Qty, 1, 10);
        if (product.Stock < qty) return SaveResult.Error($"Only {Math.Max(product.Stock, 0)} left in stock.");

        var zone = await db.DeliveryZones.AsNoTracking().FirstOrDefaultAsync(z => z.Id == input.ZoneId && z.IsActive, ct);
        if (zone is null) return SaveResult.Error("Choose a delivery zone.");
        if (method == PaymentMethods.PayOnDelivery && !zone.AllowPayOnDelivery)
            return SaveResult.Error("Pay on delivery is not available for that zone.");
        if (method == PaymentMethods.PayOnDelivery && input.BnplCount is not null)
            return SaveResult.Error("Installments cannot be combined with pay on delivery.");

        if (input.BnplCount is not null)
        {
            var state = await credit.StateAsync(uid, ct);
            if (state.PlanCount >= MaxActivePlans)
                return SaveResult.Error("You already have two active installment plans. Finish one first.");
            if (state.Blocked)
                return SaveResult.Error("Catch up on your overdue installments before starting another plan.");
        }

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var unit = Pricing.Display(product.Price, product.DiscountPercent);
        var goodsTotal = unit * qty;
        var insured = input.Insurance && product.IsInsurable;
        var premium = insured ? Money(product.Price * Pricing.InsurancePremiumPercent) : 0m;
        var total = goodsTotal + zone.Fee + premium;

        var reserved = await db.Products.IgnoreQueryFilters()
            .Where(p => p.Id == productId && p.Stock >= qty)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - qty).SetProperty(p => p.SoldCount, p => p.SoldCount + qty), ct);
        if (reserved == 0) return SaveResult.Error("That item just sold out. Try a smaller quantity.");

        var order = new Order
        {
            Number = await orders.NextOrderNumberAsync(ct),
            UserId = uid,
            PaymentMethod = method,
            PaymentStatus = method == PaymentMethods.PayOnDelivery ? PaymentStatuses.Pending : PaymentStatuses.Unpaid,
            Subtotal = product.Price * qty,
            DiscountTotal = product.Price * qty - goodsTotal,
            ShippingFee = zone.Fee,
            Total = total,
            ZoneId = zone.Id,
            AddressSnapshot = new OrderAddress { RecipientName = input.RecipientName!.Trim(), Phone = input.Phone!.Trim(), StreetLabel = input.StreetLabel!.Trim() },
            Notes = input.Notes,
            CheckoutAttemptId = input.CheckoutAttemptId!,
            AccessToken = OrderUtils.NewAccessToken(),
        };
        var item = new OrderItem
        {
            OrderId = order.Id,
            ProductId = product.Id,
            Name = product.Name,
            Sku = Sku(product.Brand, product.Id),
            ImageArt = $"{product.CategoryIcon}|{product.Brand}",
            UnitPrice = unit,
            Qty = qty,
            LineTotal = goodsTotal,
        };
        var payment = new Payment { OrderId = order.Id, Method = method, Amount = total };
        db.Orders.Add(order);
        db.OrderItems.Add(item);
        db.Payments.Add(payment);
        db.OrderEvents.Add(new OrderEvent { OrderId = order.Id, Status = OrderStatuses.Pending, Note = "Order placed", At = now });
        db.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id, Delta = -qty, Reason = StockReasons.Sale, OrderId = order.Id, Note = $"Checkout: {qty} × {product.Name}", At = now,
        });

        // Pay-on-delivery orders open the delivery queue right away; paid orders get theirs when they ship.
        if (method == PaymentMethods.PayOnDelivery)
            db.Deliveries.Add(new Delivery { OrderId = order.Id, ZoneId = zone.Id });
        if (product.WarrantyMonths > 0)
            db.Warranties.Add(new Warranty { OrderItemId = item.Id, UserId = uid, StartOn = today, EndOn = today.AddMonths(product.WarrantyMonths) });
        if (insured)
            db.DeviceInsurances.Add(new DeviceInsurance
            {
                UserId = uid,
                OrderId = order.Id,
                ProductId = product.Id,
                DeviceName = product.Name,
                Premium = premium,
                CoverageAmount = Money(product.Price * Pricing.InsuranceCoveragePercent),
                StartOn = today,
                EndOn = today.AddYears(1),
            });
        if (input.BnplCount is { } count)
            AddPlan(order, count, total, now);

        await db.SaveChangesAsync(ct);

        // Development has no real gateway: card and mobile money settle instantly.
        if (env.IsDevelopment() && method != PaymentMethods.PayOnDelivery)
        {
            payment.Status = PaymentStatuses.Paid;
            payment.PaidAt = now;
            payment.ProviderTxRef = $"simulated-{now.Ticks}";
            await db.SaveChangesAsync(ct);
            if (!await orders.TransitionAsync(order, OrderStatuses.Paid, "Simulated card payment (dev)", uid, ct))
                return SaveResult.Error("Payment could not be confirmed.");
        }
        await tx.CommitAsync(ct);
        // The new order changes every sales KPI and both storefront rails, and its stock change can alter related products.
        await cache.RemoveByTagAsync(StoreCache.Sales, ct);
        await cache.RemoveByTagAsync(StoreCache.Catalog, ct);
        await cache.RemoveByTagAsync(StoreCache.Home, ct);
        await cache.RemoveByTagAsync(StoreCache.Product(productId), ct);

        // Read after commit: concurrent checkouts each subtract from the real row, not from our earlier snapshot.
        var stockNow = await db.Products.Where(p => p.Id == productId).Select(p => p.Stock).FirstAsync(ct);
        if (alerts.ShouldAlert(productId, stockNow, product.LowStockThreshold))
            await realtime.StockAlertAsync(productId, stockNow, ct);
        return SaveResult.Success($"Order {order.Number} placed.", $"/orders/{order.Number}?t={order.AccessToken}");
    }

    /// The plan covers the whole order plus the BNPL fee, split in equal installments 14 days apart.
    void AddPlan(Order order, int count, decimal total, DateTime now)
    {
        var planTotal = Money(total * (100m + Pricing.BnplFeePercent) / 100m);
        var plan = new PayPlan
        {
            OrderId = order.Id,
            UserId = order.UserId,
            InstallmentCount = count,
            AmountPerInstallment = Money(planTotal / count),
            FeeTotal = Money(planTotal - total),
            StartedAt = now,
        };
        db.PayPlans.Add(plan);
        for (var i = 1; i <= count; i++)
            db.Installments.Add(new Installment
            {
                PlanId = plan.Id,
                Number = i,
                Amount = plan.AmountPerInstallment,
                DueDate = DateOnly.FromDateTime(now).AddDays(14 * i),
            });
    }

    static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    static string Sku(string brand, Guid productId) =>
        $"{string.Concat(brand.Where(char.IsAsciiLetterOrDigit).Take(3)).ToUpperInvariant()}-{productId.ToString("N")[..8].ToUpperInvariant()}";
}
