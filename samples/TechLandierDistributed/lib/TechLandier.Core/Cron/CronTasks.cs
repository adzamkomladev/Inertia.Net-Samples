using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Features;

namespace TechLandier.Infrastructure;

/// The scheduled jobs. Every one is safe to run twice (leases already fence overlapping runs).
public sealed class CronTasks(AppDb db, JobQueue jobs, RealtimeEvents realtime, OrderService orders, IPropCacheInvalidator cache)
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

    /// Installments due within three days: one email and one in-app alert per plan.
    public async Task BnplRemindersAsync(CancellationToken ct)
    {
        var horizon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        var due = await db.Installments.AsNoTracking()
            .Where(i => i.Status == InstallmentStatuses.Due && i.DueDate <= horizon && i.Plan.Status == PayPlanStatuses.Active
                && i.Plan.User.EmailVerifiedAt != null && i.Plan.User.Preferences.EmailInstallmentReminders)
            .OrderBy(i => i.DueDate)
            .Select(i => new { i.PlanId, i.Plan.UserId, i.Number, i.Amount, i.DueDate })
            .ToListAsync(ct);
        var next = due.GroupBy(i => i.PlanId).Select(g => g.First()).ToList();
        foreach (var i in next)
        {
            jobs.Enqueue(db, new InstallmentReminderEmail(i.PlanId, "reminder"));
            db.Alerts.Add(new Alert
            {
                UserId = i.UserId,
                Kind = AlertKinds.InstallmentDue,
                Title = $"Installment {i.Number} of {i.Amount:N2} due {i.DueDate:MMM d}",
                Body = "Pay early to keep your financing room.",
                Href = "/account/credit",
            });
        }
        await db.SaveChangesAsync(ct);
        jobs.Wake();
        await NotificationsCache.InvalidateAsync(cache, next.Select(i => i.UserId), ct);
        foreach (var i in next)
            await realtime.NotifyUserAsync(i.UserId, $"Installment due {i.DueDate:MMM d}", $"Installment {i.Number} of {i.Amount:N2} is coming up.", "/account/credit", ct);
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
        {
            if (i.Email) jobs.Enqueue(db, new InstallmentReminderEmail(i.PlanId, "overdue"));
            db.Alerts.Add(new Alert
            {
                UserId = i.UserId, Kind = AlertKinds.Overdue, Title = $"Installment {i.Number} is overdue",
                Body = "Overdue plans pause new financing until you pay.", Href = "/account/credit",
            });
        }
        foreach (var p in defaulted)
        {
            if (p.Email) jobs.Enqueue(db, new InstallmentReminderEmail(p.Id, "overdue"));
            db.Alerts.Add(new Alert
            {
                UserId = p.UserId, Kind = AlertKinds.Overdue, Title = "Plan defaulted",
                Body = "Your installment plan moved to defaulted. Settle it to restore financing.", Href = "/account/credit",
            });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        jobs.Wake();
        await NotificationsCache.InvalidateAsync(cache, newlyOverdue.Select(i => i.UserId).Concat(defaulted.Select(p => p.UserId)), ct);
        foreach (var i in newlyOverdue)
            await realtime.NotifyUserAsync(i.UserId, $"Installment {i.Number} overdue", "Overdue plans pause new financing until you pay.", "/account/credit", ct);
    }

    /// Monday statement: one email per active plan listing its open installments.
    public async Task BnplStatementAsync(CancellationToken ct)
    {
        var plans = await db.PayPlans.AsNoTracking()
            .Where(p => p.Status == PayPlanStatuses.Active && p.User.EmailVerifiedAt != null && p.User.Preferences.EmailInstallmentReminders)
            .Select(p => p.Id)
            .ToListAsync(ct);
        foreach (var id in plans) jobs.Enqueue(db, new InstallmentReminderEmail(id, "statement"));
        await db.SaveChangesAsync(ct);
        jobs.Wake();
    }

    /// Tickets past their first-response SLA move up one priority level; the ones that reach urgent alert every admin.
    public async Task TicketEscalationAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var overdue = db.Tickets.Where(t => t.ResolvedAt == null && t.DueAt < now
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

    /// Unpaid online orders older than 30 minutes cancel themselves and give their reserved stock back.
    public async Task OrderTimeoutAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        var stale = await db.Orders
            .Where(o => o.Status == OrderStatuses.Pending && o.PaymentMethod != PaymentMethods.PayOnDelivery && o.CreatedAt < cutoff)
            .ToListAsync(ct);
        foreach (var order in stale)
            if (await orders.TransitionAsync(order, OrderStatuses.Cancelled, "Unpaid order timed out", null, ct))
                jobs.Enqueue(db, new OrderCancelledEmail(order.Id, "We did not receive your payment within the checkout window."));
        await db.SaveChangesAsync(ct);
        jobs.Wake();
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
            await jobs.EnqueueAsync(db, new DeleteAssets([.. keys]), ct: ct);
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

    public async Task PruneJobsAsync(CancellationToken ct)
    {
        var done = DateTime.UtcNow.AddDays(-7);
        var failed = DateTime.UtcNow.AddDays(-30);
        await db.Jobs.Where(j => (j.Status == JobStatus.Done && j.CreatedAt < done) || (j.Status == JobStatus.Failed && j.CreatedAt < failed)).ExecuteDeleteAsync(ct);
    }
}
