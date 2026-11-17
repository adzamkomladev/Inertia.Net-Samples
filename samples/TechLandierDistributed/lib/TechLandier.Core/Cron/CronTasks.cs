using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Features;

namespace TechLandier.Infrastructure;

/// The scheduled jobs. Every one is safe to run twice (leases already fence overlapping runs).
public sealed class CronTasks(AppDb db, IJobOutbox jobs, RealtimeEvents realtime, OrderService orders, IPropCacheInvalidator cache)
{
    /// Recomputes the daily sales stats for yesterday and today straight from the order tables.
    public async Task SalesRollupAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var date in new[] { today.AddDays(-1), today })
        {
            var from = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var to = from.AddDays(1);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "SalesDailyStats" ("Date", "Revenue", "OrderCount", "BnplShare", "PickupCount", "DeliveryCount")
                VALUES ({date},
                    (SELECT COALESCE(SUM(o."Total"), 0) FROM "Orders" o WHERE o."PaidAt" >= {from} AND o."PaidAt" < {to}
                        AND o."Status" = ANY({OrderStatuses.PaidOrLater})),
                    (SELECT COUNT(*) FROM "Orders" o WHERE o."CreatedAt" >= {from} AND o."CreatedAt" < {to} AND o."Status" <> {OrderStatuses.Cancelled}),
                    (SELECT COALESCE(SUM(o."Total"), 0) FROM "Orders" o JOIN "PayPlans" pp ON pp."OrderId" = o."Id"
                        WHERE o."PaidAt" >= {from} AND o."PaidAt" < {to} AND pp."Status" = {PayPlanStatuses.Active}),
                    (SELECT COUNT(*) FROM "Orders" o WHERE o."PaidAt" >= {from} AND o."PaidAt" < {to} AND o."PaymentMethod" = {PaymentMethods.PayOnDelivery}),
                    (SELECT COUNT(*) FROM "Deliveries" d WHERE d."DeliveredAt" >= {from} AND d."DeliveredAt" < {to}))
                ON CONFLICT ("Date") DO UPDATE SET
                    "Revenue" = EXCLUDED."Revenue", "OrderCount" = EXCLUDED."OrderCount", "BnplShare" = EXCLUDED."BnplShare",
                    "PickupCount" = EXCLUDED."PickupCount", "DeliveryCount" = EXCLUDED."DeliveryCount"
                """, ct);
        }
    }

    /// Installments due within three days: one email and one in-app alert per plan, written and staged in one transaction.
    public async Task BnplRemindersAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var horizon = today.AddDays(3);
        var due = await db.Installments.AsNoTracking()
            .Where(i => i.Status == InstallmentStatuses.Due && i.DueDate <= horizon && i.Plan.Status == PayPlanStatuses.Active
                && i.Plan.User.EmailVerifiedAt != null && i.Plan.User.Preferences.EmailInstallmentReminders)
            .OrderBy(i => i.DueDate)
            .Select(i => new { i.PlanId, i.Plan.UserId, i.Number, i.Amount, i.DueDate })
            .ToListAsync(ct);
        var next = due.GroupBy(i => i.PlanId).Select(g => g.First()).ToList();
        if (next.Count == 0) return;

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            foreach (var i in next)
                db.Alerts.Add(new Alert
                {
                    UserId = i.UserId,
                    Kind = AlertKinds.InstallmentDue,
                    Title = $"Installment {i.Number} of {i.Amount:N2} due {i.DueDate:MMM d}",
                    Body = "Pay early to keep your financing room.",
                    Href = "/account/credit",
                });
            await db.SaveChangesAsync(ct);
            await jobs.StageManyAsync(db, [.. next.Select(i => new InstallmentReminderEmail(i.PlanId, "reminder", Day(today)))], ct);
            await tx.CommitAsync(ct);
        }
        await NotificationsCache.InvalidateAsync(cache, next.Select(i => i.UserId), ct);
        await realtime.PushAsync([.. next.Select(i => RealtimeEvents.Notification(i.UserId, $"Installment due {i.DueDate:MMM d}", $"Installment {i.Number} of {i.Amount:N2} is coming up.", "/account/credit"))], ct);
    }

    /// Flags installments past their due date as overdue, and defaults plans with an installment overdue for two weeks.
    public async Task BnplOverdueAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var defaultCutoff = today.AddDays(-14);
        var late = db.Installments.Where(i => i.Status == InstallmentStatuses.Due && i.DueDate < today);
        var defaulting = db.PayPlans.Where(p => p.Status == PayPlanStatuses.Active
            && db.Installments.Any(i => i.PlanId == p.Id && i.Status == InstallmentStatuses.Overdue && i.DueDate < defaultCutoff));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var newlyOverdue = await late.AsNoTracking()
            .Select(i => new { i.PlanId, i.Plan.UserId, i.Number, Email = i.Plan.User.Preferences.EmailInstallmentReminders })
            .ToListAsync(ct);
        var defaulted = await defaulting.AsNoTracking()
            .Select(p => new { p.Id, p.UserId, Email = p.User.Preferences.EmailInstallmentReminders })
            .ToListAsync(ct);
        await late.ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InstallmentStatuses.Overdue), ct);
        await defaulting.ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PayPlanStatuses.Defaulted), ct);

        foreach (var i in newlyOverdue)
            db.Alerts.Add(new Alert
            {
                UserId = i.UserId, Kind = AlertKinds.Overdue, Title = $"Installment {i.Number} is overdue",
                Body = "Overdue plans pause new financing until you pay.", Href = "/account/credit",
            });
        foreach (var p in defaulted)
            db.Alerts.Add(new Alert
            {
                UserId = p.UserId, Kind = AlertKinds.Overdue, Title = "Plan defaulted",
                Body = "Your installment plan moved to defaulted. Settle it to restore financing.", Href = "/account/credit",
            });
        await db.SaveChangesAsync(ct);
        // One email per plan, however many of its installments went overdue or whether it also defaulted.
        var mailPlans = newlyOverdue.Where(i => i.Email).Select(i => i.PlanId).Concat(defaulted.Where(p => p.Email).Select(p => p.Id)).Distinct();
        await jobs.StageManyAsync(db, [.. mailPlans.Select(plan => new InstallmentReminderEmail(plan, "overdue", Day(today)))], ct);
        await tx.CommitAsync(ct);

        await NotificationsCache.InvalidateAsync(cache, newlyOverdue.Select(i => i.UserId).Concat(defaulted.Select(p => p.UserId)), ct);
        await realtime.PushAsync([.. newlyOverdue.Select(i => RealtimeEvents.Notification(i.UserId, $"Installment {i.Number} overdue", "Overdue plans pause new financing until you pay.", "/account/credit"))], ct);
    }

    /// Monday statement: one email per customer listing the open installments of all their active plans.
    public async Task BnplStatementAsync(CancellationToken ct)
    {
        var day = Day(DateOnly.FromDateTime(DateTime.UtcNow));
        var users = await db.PayPlans.AsNoTracking()
            .Where(p => p.Status == PayPlanStatuses.Active && p.User.EmailVerifiedAt != null && p.User.Preferences.EmailInstallmentReminders)
            .Select(p => p.UserId)
            .Distinct()
            .ToListAsync(ct);
        // Nothing is written, so the jobs are dispatched directly (a re-run reuses the same-day jobs).
        await jobs.DispatchManyAsync([.. users.Select(id => new InstallmentStatementEmail(id, day))], ct);
    }

    static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    /// Tickets past their first-response SLA move up one priority level; the ones that reach urgent alert every admin.
    public async Task TicketEscalationAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var overdue = db.Tickets.Where(t => t.ResolvedAt == null && t.FirstResponseAt == null && t.DueAt < now
            && (t.Status == TicketStatuses.Received || t.Status == TicketStatuses.Open));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var newlyUrgent = await overdue.Where(t => t.Priority == TicketPriorities.High).Select(t => t.Number).ToListAsync(ct);
        // Highest first, so a ticket is bumped only one level.
        foreach (var (current, bumped) in new[]
        {
            (TicketPriorities.Urgent, TicketPriorities.Urgent), (TicketPriorities.High, TicketPriorities.Urgent),
            (TicketPriorities.Normal, TicketPriorities.High), (TicketPriorities.Low, TicketPriorities.Normal),
        })
            await overdue.Where(t => t.Priority == current)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Priority, bumped).SetProperty(t => t.UpdatedAt, now), ct);

        List<Guid> admins = [];
        if (newlyUrgent.Count > 0)
        {
            admins = await db.Users.Where(u => u.Role == "admin").Select(u => u.Id).ToListAsync(ct);
            foreach (var admin in admins)
                foreach (var number in newlyUrgent)
                    db.Alerts.Add(new Alert
                    {
                        UserId = admin, Kind = AlertKinds.Reply, Title = $"Ticket {number} overdue",
                        Body = "An urgent complaint passed its first-response SLA.", Href = "/admin/support",
                    });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        if (newlyUrgent.Count > 0)
        {
            await NotificationsCache.InvalidateAsync(cache, admins, ct);
            await realtime.AdminPulseAsync($"{newlyUrgent.Count} ticket(s) overdue", "Urgent complaints passed their first-response SLA.", "/admin/support", ct);
        }
    }

    /// Unpaid online orders older than 30 minutes cancel themselves and give their reserved stock back. Each batch of
    /// orders shares one transaction: cancellation, timeline, alert, stock release and the staged cancel email commit together.
    public async Task OrderTimeoutAsync(CancellationToken ct)
    {
        const int batchSize = 200;
        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        for (var batch = 0; batch < 50; batch++) // safety cap; the rest goes next hour
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var stale = await db.Orders
                .Where(o => o.Status == OrderStatuses.Pending && o.PaymentMethod != PaymentMethods.PayOnDelivery && o.CreatedAt < cutoff)
                .OrderBy(o => o.CreatedAt).ThenBy(o => o.Id)
                .Take(batchSize)
                .ToListAsync(ct);
            if (stale.Count == 0) return;

            var userIds = stale.Select(o => o.UserId).Distinct().ToList();
            var admins = (await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id) && u.Role == "admin").Select(u => u.Id).ToListAsync(ct)).ToHashSet();
            var moves = new List<OrderMove>();
            foreach (var order in stale)
                if (await orders.ApplyAsync(order, OrderStatuses.Cancelled, "Unpaid order timed out", null,
                        "We did not receive your payment within the checkout window.", releaseStock: false, ct, userIsAdmin: admins.Contains(order.UserId)) is { } move)
                    moves.Add(move);
            await orders.ReleaseStockAsync([.. moves.Select(m => m.Order)], DateTime.UtcNow, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await orders.AfterCommitAsync(moves, invalidateTags: true, ct);
            if (moves.Count == 0 || stale.Count < batchSize) return;
        }
    }
    public async Task PruneViewsAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);
        int deleted;
        do
        {
            deleted = await db.Database.ExecuteSqlAsync($"DELETE FROM \"ProductViews\" WHERE \"Id\" IN (SELECT \"Id\" FROM \"ProductViews\" WHERE \"At\" < {cutoff} ORDER BY \"Id\" LIMIT 5000)", ct);
            await Task.Delay(50, ct); // let request-path writers get the lock between chunks
        } while (deleted == 5000);
    }

    /// Removes asset rows nobody references (stale pending uploads, unattached ready ones) and queues deleting their objects.
    public async Task OrphanAssetsAsync(CancellationToken ct)
    {
        var pendingBefore = DateTime.UtcNow.AddHours(-1);
        var readyBefore = DateTime.UtcNow.AddHours(-24);
        for (var i = 0; i < 50; i++) // safety cap; the rest goes next hour
        {
            // The reference re-check sits in the DELETE itself, so an asset attached meanwhile survives. The object deletion
            // is a durable job committed in the same transaction, so a storage outage only delays it.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var keys = await db.Database.SqlQuery<string>($"""
                WITH candidates AS (
                    SELECT a."Id" FROM "Assets" a
                    WHERE (a."Status" = 'Pending' AND a."CreatedAt" < {pendingBefore})
                        OR (a."Status" = 'Ready' AND a."CreatedAt" < {readyBefore}
                            AND NOT EXISTS (SELECT 1 FROM "Products" p WHERE p."ImageAssetId" = a."Id")
                            AND NOT EXISTS (SELECT 1 FROM "ProductImages" pi WHERE pi."AssetId" = a."Id")
                            AND NOT EXISTS (SELECT 1 FROM "Users" u WHERE u."AvatarAssetId" = a."Id"))
                    ORDER BY a."Id" LIMIT 200 FOR UPDATE SKIP LOCKED
                )
                DELETE FROM "Assets" a USING candidates c
                WHERE a."Id" = c."Id"
                    AND ((a."Status" = 'Pending' AND a."CreatedAt" < {pendingBefore})
                        OR (a."Status" = 'Ready' AND a."CreatedAt" < {readyBefore}
                            AND NOT EXISTS (SELECT 1 FROM "Products" p WHERE p."ImageAssetId" = a."Id")
                            AND NOT EXISTS (SELECT 1 FROM "ProductImages" pi WHERE pi."AssetId" = a."Id")
                            AND NOT EXISTS (SELECT 1 FROM "Users" u WHERE u."AvatarAssetId" = a."Id")))
                RETURNING a."Key" AS "Value"
                """).ToListAsync(ct);
            if (keys.Count == 0) return;
            await jobs.StageAsync(db, new DeleteAssets([.. keys]), ct);
            await tx.CommitAsync(ct);
        }
    }

    /// Unverified accounts older than a week that never bought or booked anything.
    public async Task PruneUnverifiedAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        await db.Users
            .Where(u => u.EmailVerifiedAt == null && u.CreatedAt < cutoff && !db.Orders.Any(o => o.UserId == u.Id) && !db.RepairTickets.Any(r => r.UserId == u.Id))
            .ExecuteDeleteAsync(ct);
    }
}
