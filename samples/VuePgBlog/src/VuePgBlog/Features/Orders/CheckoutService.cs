using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Infrastructure;

namespace VuePgBlog.Features;

/// Buybox + idempotent checkout. One product per order (no cart in the storefront), stock via conditional
/// UPDATE WHERE Stock >= qty inside the transaction so overselling races lose cleanly.
public static partial class CheckoutService
{
    internal static async Task<IResult> BuyAsync(Guid productId, int? qty, string? bnpl, bool? insure, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var uid = ctx.User.UserId();
        var slug = await db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == productId).Select(p => p.Slug).FirstOrDefaultAsync(ct) ?? "";
        var product = await StoreService.LoadDetail(db, slug, ct);
        if (product is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);

        // BNPL visibility uses the same guardrails checkout enforces.
        var credit = await db.Users.AsNoTracking()
            .Where(u => u.Id == uid)
            .Select(u => new
            {
                PlanCount = db.PayPlans.Count(p => p.UserId == uid && p.Status == PayPlanStatuses.Active),
                Blocked = db.Installments.Any(i => (i.Plan.UserId == uid && i.Status == InstallmentStatuses.Overdue)
                    || (i.Plan.Status == PayPlanStatuses.Active && i.Status == InstallmentStatuses.Due && i.DueDate < DateOnly.FromDateTime(DateTime.UtcNow))),
            })
            .FirstOrDefaultAsync(ct);
        if (credit is null)
            return Results.Redirect("/login");
        return Render(
            "products/Buy",
            new InertiaProps
            {
                ["product"] = product,
                ["qty"] = Math.Clamp(qty ?? 1, 1, 10),
                ["zones"] = await db.DeliveryZones.AsNoTracking()
                    .Where(z => z.IsActive)
                    .OrderBy(z => z.SortOrder)
                    .Select(z => new ZoneRow(z.Name, z.Fee, z.EtaText, z.AllowPayOnDelivery))
                    .ToListAsync(ct),
                ["insure"] = insure ?? false,
                ["bnpl"] = bnpl,
                ["creditState"] = new { credit.PlanCount, credit.Blocked },
            }
        );
    }

    internal static async Task<IResult> CreateAsync(
        CheckoutInput input,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        RealtimeEvents realtime,
        StockAlerts alerts,
        IHostEnvironment env,
        CancellationToken ct)
    {
        var uid = ctx.User.UserId();
        var method = input.PaymentMethod is PaymentMethods.Card or PaymentMethods.MobileMoney or PaymentMethods.PayOnDelivery
            ? input.PaymentMethod!
            : null;
        if (method is null || input.ProductId is not { } productId)
            return BadFlash("Choose a payment method.");
        if (input.BnplCount is not (null or 3 or 6 or 12))
            return BadFlash("Installments can only be 3, 6 or 12.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Per-user advisory lock: serializes plan-limit checks and makes the idempotency retry safe.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({uid.ToString()}))", ct);

        // Idempotent re-submit: same attempt id → same order, no second charge.
        var existing = await db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.UserId == uid && o.CheckoutAttemptId == input.CheckoutAttemptId, ct);
        if (existing is not null)
        {
            await tx.CommitAsync(ct);
            ctx.Inertia().Flash("toast", new Toast("info", "This order was already placed."));
            return Results.Redirect($"/orders/{existing.Number}?t={existing.AccessToken}");
        }

        var product = await db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new
            {
                p.Id,
                p.Slug,
                p.Name,
                p.Brand,
                p.Price,
                p.DiscountPercent,
                p.Stock,
                p.LowStockThreshold,
                p.WarrantyMonths,
                p.IsInsurable,
                p.Status,
                CategoryIcon = p.Category.Icon,
            })
            .FirstOrDefaultAsync(ct);
        if (product is null || product.Status != ProductStatuses.Active)
            return BadFlash("That product is no longer available.");
        var qty = Math.Clamp(input.Qty, 1, 10);
        if (product.Stock < qty)
            return BadFlash($"Only {Math.Max(product.Stock, 0)} left in stock.");

        if (await db.DeliveryZones.AsNoTracking().FirstOrDefaultAsync(z => z.Id == input.ZoneId && z.IsActive, ct) is not { } zone)
            return BadFlash("Choose a delivery zone.");
        if (method == PaymentMethods.PayOnDelivery && !zone.AllowPayOnDelivery)
            return BadFlash("Pay on delivery is not available for that zone.");

        // BNPL guardrails before any writes.
        if (input.BnplCount is { } count)
        {
            var activePlans = await db.PayPlans.CountAsync(p => p.UserId == uid && p.Status == PayPlanStatuses.Active, ct);
            if (activePlans >= 2)
                return BadFlash("You already have two active installment plans. Finish one first.");
            if (await db.Installments.AnyAsync(i => i.Plan.UserId == uid && i.Status == InstallmentStatuses.Overdue, ct))
                return BadFlash("Catch up on your overdue installments before starting another plan.");
        }

        var now = DateTime.UtcNow;
        var unit = Pricing.Display(product.Price, product.DiscountPercent);
        var listTotal = product.Price * qty;
        var goodsTotal = unit * qty;
        var discountTotal = listTotal - goodsTotal;
        var premiumTotal = 0m;
        if (input.Insure && product.IsInsurable)
            premiumTotal = Math.Round(product.Price * Pricing.InsurancePremiumPercent, 2, MidpointRounding.AwayFromZero);
        var shipping = zone.Fee;
        var total = goodsTotal + shipping + premiumTotal;

        // Conditional stock reservation: the loser of the race sees zero updated rows.
        var reserved = await db.Products.IgnoreQueryFilters()
            .Where(p => p.Id == productId && p.Stock >= qty)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Stock, p => p.Stock - qty)
                .SetProperty(p => p.SoldCount, p => p.SoldCount + qty), ct);
        if (reserved == 0)
            return BadFlash("Only " + Math.Max(await db.Products.IgnoreQueryFilters().Where(p => p.Id == productId)
                .Select(p => p.Stock).FirstAsync(ct), 0) + " left in stock.");

        var order = new Order
        {
            Number = await OrderNumbers.NextOrderAsync(db, ct),
            UserId = uid,
            Status = OrderStatuses.Pending,
            PaymentMethod = method,
            PaymentStatus = method == PaymentMethods.PayOnDelivery ? PaymentStatuses.Pending : PaymentStatuses.Unpaid,
            Subtotal = listTotal,
            DiscountTotal = discountTotal,
            ShippingFee = shipping,
            Total = total,
            ZoneId = zone.Id,
            AddressSnapshot = new OrderAddress
            {
                RecipientName = input.RecipientName!.Trim(),
                Phone = input.Phone!.Trim(),
                StreetLabel = input.StreetLabel!.Trim(),
            },
            Notes = input.Notes,
            CheckoutAttemptId = input.CheckoutAttemptId!,
            AccessToken = OrderNumbers.Access(),
        };
        db.Orders.Add(order);
        db.OrderItems.Add(new OrderItem
        {
            OrderId = order.Id,
            ProductId = product.Id,
            Name = product.Name,
            Sku = Sku(product.Brand, product.Id),
            ImageArt = $"{product.CategoryIcon}|{product.Brand}",
            UnitPrice = unit,
            Qty = qty,
            LineTotal = goodsTotal,
        });
        var orderIdItem = db.OrderItems.Local.First(i => i.OrderId == order.Id && i.ProductId == product.Id);
        db.OrderEvents.Add(new OrderEvent { OrderId = order.Id, Status = OrderStatuses.Pending, Note = "Order placed", At = now });
        db.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id,
            Delta = -qty,
            Reason = StockReasons.Sale,
            OrderId = order.Id,
            Note = $"Checkout: {qty} × {product.Name}",
            At = now,
        });

        var payment = new Payment
        {
            OrderId = order.Id,
            Method = method,
            Amount = total,
            Currency = Pricing.Currency,
            Status = PaymentStatuses.Pending,
        };
        db.Payments.Add(payment);

        // Pay-on-delivery orders open the delivery queue right away; paid orders get theirs when they ship.
        if (method == PaymentMethods.PayOnDelivery)
            db.Deliveries.Add(new Delivery { OrderId = order.Id, ZoneId = zone.Id });

        // Auto warranty for items whose product carries one.
        if (product.WarrantyMonths > 0)
            db.Warranties.Add(new Warranty
            {
                OrderItemId = orderIdItem.Id,
                UserId = uid,
                StartOn = DateOnly.FromDateTime(now),
                EndOn = DateOnly.FromDateTime(now).AddMonths(product.WarrantyMonths),
            });

        if (input.Insure && product.IsInsurable)
            db.DeviceInsurances.Add(new DeviceInsurance
            {
                UserId = uid,
                OrderId = order.Id,
                ProductId = product.Id,
                DeviceName = product.Name,
                Premium = premiumTotal,
                CoverageAmount = Math.Round(product.Price * Pricing.InsuranceCoveragePercent, 2, MidpointRounding.AwayFromZero),
                StartOn = DateOnly.FromDateTime(now),
                EndOn = DateOnly.FromDateTime(now).AddYears(1),
            });

        PayPlan? plan = null;
        if (input.BnplCount is { } planCount)
        {
            var planTotal = Math.Round(total * (100m + Pricing.BnplFeePercent) / 100m, 2, MidpointRounding.AwayFromZero);
            plan = new PayPlan
            {
                OrderId = order.Id,
                UserId = uid,
                InstallmentCount = planCount,
                AmountPerInstallment = Math.Round(planTotal / planCount, 2, MidpointRounding.AwayFromZero),
                FeeTotal = Math.Round(planTotal - total, 2, MidpointRounding.AwayFromZero),
                Status = PayPlanStatuses.Active,
                StartedAt = now,
            };
            db.PayPlans.Add(plan);
            for (var i = 1; i <= planCount; i++)
                db.Installments.Add(new Installment
                {
                    PlanId = plan.Id,
                    Number = i,
                    Amount = plan.AmountPerInstallment,
                    DueDate = DateOnly.FromDateTime(now).AddDays(14 * i),
                });
        }

        await db.SaveChangesAsync(ct);

        // Simulated gateway in Development: card/mobile money settle instantly (clearly marked).
        var simulated = env.IsDevelopment() && method != PaymentMethods.PayOnDelivery;
        if (simulated)
        {
            payment.Status = PaymentStatuses.Paid;
            payment.PaidAt = now;
            payment.ProviderTxRef = $"simulated-{now.Ticks}";
            await db.SaveChangesAsync(ct);
            if (!await OrderWorkflow.MoveAsync(db, order, OrderStatuses.Paid, "Simulated card payment (dev)", uid, jobs, realtime, ct))
                return BadFlash("Payment could not be confirmed.");
        }

        await tx.CommitAsync(ct);

        // Stock alerts: SignalR-only, once per crossing across the whole app instance.
        var stockNow = await db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => p.Stock)
            .FirstAsync(ct);
        if (alerts.ShouldAlert(productId, stockNow, product.LowStockThreshold))
            await realtime.StockAlertAsync(productId, stockNow, ct);

        ctx.Inertia().Flash("toast", new Toast("success", $"Order {order.Number} placed."));
        return Results.Redirect($"/orders/{order.Number}?t={order.AccessToken}");
        static IResult BadFlash(string message) =>
            Back().WithFlash("toast", new Toast("error", message));
    }

    internal static string Sku(string brand, Guid productId)
    {
        var prefix = new string(brand.Where(char.IsAsciiLetterOrDigit).ToArray(), 0, Math.Min(3, brand.Length)).ToUpperInvariant();
        return $"{prefix}-{productId.ToString("N")[..8].ToUpperInvariant()}";
    }
}
