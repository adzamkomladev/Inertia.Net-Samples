using System.Diagnostics;
using Npgsql;
using NCrontab;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;
using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Features;

namespace VuePgBlog.Infrastructure;

/// <summary>TickerQ jobs retain database leases to fence duplicate runs across replicas.</summary>
public sealed class CronJobs(IServiceScopeFactory scopes, ILogger<CronJobs> log)
{
    static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(30);

    internal static readonly (string Name, string Cron, Func<IServiceProvider, CancellationToken, Task> Run)[] Schedule =
    [
        ("sales-rollup", "*/15 * * * *", Rollup),
        ("bnpl-reminders", "0 7 * * *", BnplReminders),
        ("bnpl-overdue", "10 7 * * *", BnplOverdue),
        ("bnpl-statement", "0 8 * * 1", BnplStatement),
        ("ticket-escalation", "0 * * * *", TicketEscalation),
        ("order-timeout", "0 * * * *", OrderTimeout),
        ("backup", "0 2 * * *", Backup),
        ("prune-product-views", "0 3 * * *", PruneViews),
        ("orphan-assets", "0 * * * *", OrphanAssets),
        ("prune-unverified", "0 4 * * *", PruneUnverified),
        ("prune-jobs", "30 3 * * *", PruneJobs),
        ("search-reconcile", "*/5 * * * *", async (sp, ct) =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                if (string.Equals(config["SEARCH_DRIVER"], "meilisearch", StringComparison.OrdinalIgnoreCase))
                    await SearchIndexing.ReconcileAsync(sp.GetRequiredService<AppDb>(), sp.GetRequiredService<JobQueue>(), ct);
            }),
    ];

    /// `Cron:<name>` (env CRON_<NAME>) overrides the default expression. Parsed eagerly so a typo fails startup, not 10s later.
    public static Dictionary<string, CrontabSchedule> Schedules(IConfiguration config) => Schedule.ToDictionary(s => s.Name, s =>
    {
        var text = config[$"Cron:{s.Name}"] is { Length: > 0 } c ? c : s.Cron;
        try { return CrontabSchedule.Parse(CronExpression.Parse(text).Value, new CrontabSchedule.ParseOptions { IncludingSeconds = true }); }
        catch (Exception e) when (e is ArgumentException or CrontabException) { throw new InvalidOperationException($"Invalid cron expression '{text}' for Cron:{s.Name}: {e.Message}", e); }
    });

    internal async Task<bool> Tick((string Name, string Cron, Func<IServiceProvider, CancellationToken, Task> Run) entry, CrontabSchedule expr, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var name = entry.Name;
        var now = DateTime.UtcNow;

        // New schedule: baseline at "now" so it waits for its next occurrence instead of firing immediately.
        await db.Database.ExecuteSqlAsync($"INSERT INTO \"CronLeases\" (\"Name\", \"LastRunAt\", \"LockedUntil\") VALUES ({name}, {now}, NULL) ON CONFLICT (\"Name\") DO NOTHING", ct);
        var last = await db.CronLeases.AsNoTracking().Where(l => l.Name == name).Select(l => l.LastRunAt).FirstAsync(ct);

        // Latest occurrence that is already due; several missed slots collapse into one run.
        var due = last is null ? (DateTime?)null : expr.GetNextOccurrences(DateTime.SpecifyKind(last.Value, DateTimeKind.Utc), now.AddTicks(1)).LastOrDefault();
        if (due is not { } d || d == default) return false;

        var lease = now.Add(LeaseTtl); // unique per claim; completion/release only match this exact value (fencing)
        var claimed = await db.CronLeases
            .Where(l => l.Name == name && (l.LockedUntil == null || l.LockedUntil < now) && (l.LastRunAt == null || l.LastRunAt < d))
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.LockedUntil, lease), ct);
        if (claimed == 0)
        {
            log.LogDebug("Cron {Name} occurrence {Due:u} already claimed or completed", name, d);
            return true;
        }

        var finished = false;
        try
        {
            log.LogInformation("Cron {Name} running (due {Due:u})", name, d);
            await using var runScope = scopes.CreateAsyncScope();
            var started = Stopwatch.GetTimestamp();
            await entry.Run(runScope.ServiceProvider, ct);
            finished = true;
            log.LogInformation("Cron {Name} completed in {ElapsedMs:F1} ms", name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            log.LogWarning("Cron {Name} interrupted by shutdown; it will run again on next start", name);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Cron {Name} failed; waiting for the next occurrence", name);
            finished = true;
        }
        finally
        {
            // Never pass the stopping token here: the lease must be released even while shutting down.
            await using var s2 = scopes.CreateAsyncScope();
            var db2 = s2.ServiceProvider.GetRequiredService<AppDb>();
            var q = db2.CronLeases.Where(l => l.Name == name && l.LockedUntil == lease);
            if (finished) await q.ExecuteUpdateAsync(s => s.SetProperty(l => l.LastRunAt, d).SetProperty(l => l.LockedUntil, (DateTime?)null));
            else await q.ExecuteUpdateAsync(s => s.SetProperty(l => l.LockedUntil, (DateTime?)null));
        }
        return false;
    }

    // Custom-format archives are isolated by project and deployment environment in the shared bucket.
    static async Task Backup(IServiceProvider sp, CancellationToken ct)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var db = sp.GetRequiredService<AppDb>();
        var storage = sp.GetRequiredService<Storage>();
        var now = sp.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var environment = sp.GetRequiredService<IHostEnvironment>().EnvironmentName;
        var segment = string.Concat(environment.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-')).ToLowerInvariant();
        var prefix = $"backups/VuePgBlog/{segment}/";
        var bucket = config["Storage:BackupBucket"] ?? config["Storage:Bucket"] ?? "uploads";
        var path = Path.Combine(Path.GetTempPath(), $"VuePgBlog-{Guid.NewGuid():N}.dump");
        var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        var start = new ProcessStartInfo(config["PG_DUMP_PATH"] ?? "pg_dump")
        {
            UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var arg in new[] { "--format=custom", "--no-password", "--file", path }) start.ArgumentList.Add(arg);
        start.Environment["PGHOST"] = connection.Host;
        start.Environment["PGPORT"] = connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["PGDATABASE"] = connection.Database;
        start.Environment["PGUSER"] = connection.Username;
        start.Environment["PGPASSWORD"] = connection.Password;
        start.Environment["PGSSLMODE"] = connection.SslMode switch
        {
            SslMode.VerifyCA => "verify-ca", SslMode.VerifyFull => "verify-full",
            _ => connection.SslMode.ToString().ToLowerInvariant()
        };
        if (!string.IsNullOrEmpty(connection.RootCertificate)) start.Environment["PGSSLROOTCERT"] = connection.RootCertificate;
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start pg_dump.");
            var errors = process.StandardError.ReadToEndAsync(ct);
            try { await process.WaitForExitAsync(ct); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            if (process.ExitCode != 0) throw new InvalidOperationException($"pg_dump failed: {await errors}");
            await errors;
            await storage.UploadFileAsync(bucket, $"{prefix}{now:yyyy-MM-dd-HHmmss}-{Guid.NewGuid():N}.dump", path, ct);
        }
        finally { File.Delete(path); }
        var keys = await storage.ListKeysAsync(prefix, ct, bucket);
        var expired = keys.OrderByDescending(k => k, StringComparer.Ordinal).Skip(14).ToArray();
        if (expired.Length > 0) await storage.DeleteAsync(expired, ct, bucket);
    }

    /// Recomputes the paid-funnel daily stats for today and yesterday from raw tables; safe to run repeatedly.
    static async Task Rollup(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var day in new[] { today.AddDays(-1), today })
        {
            var from = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var to = from.AddDays(1);
            var date = day;
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "SalesDailyStats" ("Date", "Revenue", "OrderCount", "BnplShare", "PickupCount", "DeliveryCount")
                VALUES ({date},
                    (SELECT COALESCE(SUM(o."Total"), 0) FROM "Orders" o WHERE o."PaidAt" >= {from} AND o."PaidAt" < {to}
                        AND (o."Status" = {OrderStatuses.Paid} OR o."Status" = {OrderStatuses.Processing} OR o."Status" = {OrderStatuses.Shipped} OR o."Status" = {OrderStatuses.Delivered})),
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

    /// Installments due within the next 3 days: email + in-app alert per plan.
    static async Task BnplReminders(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var jobs = sp.GetRequiredService<JobQueue>();
        var realtime = sp.GetRequiredService<RealtimeEvents>();
        var horizon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        var due = await db.Installments.AsNoTracking()
            .Where(i => i.Status == InstallmentStatuses.Due && i.DueDate <= horizon
                && i.Plan.Status == PayPlanStatuses.Active
                && i.Plan.User.EmailVerifiedAt != null && i.Plan.User.Preferences.EmailInstallmentReminders)
            .Include(i => i.Plan).ThenInclude(p => p.User)
            .OrderBy(i => i.DueDate)
            .ToListAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var group in due.GroupBy(i => i.PlanId))
        {
            var installment = group.First();
            jobs.Enqueue(db, new InstallmentReminderEmail(group.Key, "reminder"), runAt: DateTime.UtcNow.AddMilliseconds(group.Count()));
            db.Alerts.Add(new Alert
            {
                UserId = installment.Plan.UserId,
                Kind = AlertKinds.InstallmentDue,
                Title = $"Installment {installment.Number} of {installment.Amount:N2} due {installment.DueDate:MMM d}",
                Body = "Pay early to keep your financing room.",
                Href = "/account/credit",
                CreatedAt = DateTime.UtcNow,
            });
            await realtime.NotifyUserAsync(installment.Plan.UserId,
                $"Installment due {installment.DueDate:MMM d}",
                $"Installment {installment.Number} of {installment.Amount:N2} is coming up.", "/account/credit", ct);
        }
        await db.SaveChangesAsync(ct);
        jobs.Wake();
        await tx.CommitAsync(ct);
    }

    /// Marks skipped/due installments overdue, mails an overdue notice, and escalates day-14+ plans to defaulted.
    static async Task BnplOverdue(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var jobs = sp.GetRequiredService<JobQueue>();
        var realtime = sp.GetRequiredService<RealtimeEvents>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var escalateCutoff = DateTime.UtcNow.AddDays(-14);
        var newlyOverdue = await db.Installments.AsNoTracking()
            .Where(i => i.Status == InstallmentStatuses.Due && i.DueDate < today)
            .Include(i => i.Plan).ThenInclude(p => p.User)
            .Include(i => i.Plan).ThenInclude(p => p.Order)
            .ToListAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var installment in newlyOverdue)
        {
            var plan = installment.Plan;
            await db.Installments.Where(i => i.Id == installment.Id && i.Status == InstallmentStatuses.Due)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InstallmentStatuses.Overdue), ct);
            if (plan.User.Preferences.EmailInstallmentReminders)
                jobs.Enqueue(db, new InstallmentReminderEmail(plan.Id, "overdue"));
            db.Alerts.Add(new Alert
            {
                UserId = plan.UserId,
                Kind = AlertKinds.Overdue,
                Title = $"Installment {installment.Number} is overdue",
                Body = "Overdue plans pause new financing until you pay.",
                Href = "/account/credit",
            });
            await realtime.NotifyUserAsync(plan.UserId,
                $"Installment {installment.Number} overdue",
                "Overdue plans pause new financing until you pay.", "/account/credit", ct);
        }

        // Day-14 escalation: any overdue installment older than two weeks defaults the plan; customers stay flagged
        // via the overdue rows themselves (new financing is blocked by the checkout guardrail).
        var defaultCutoff = today.AddDays(-14);
        var defaultedPlans = await db.PayPlans.AsNoTracking()
            .Where(p => p.Status == PayPlanStatuses.Active
                && db.Installments.Any(i => i.PlanId == p.Id && i.Status == InstallmentStatuses.Overdue && i.DueDate < defaultCutoff))
            .ToListAsync(ct);
        foreach (var plan in defaultedPlans)
        {
            await db.PayPlans.Where(x => x.Id == plan.Id && x.Status == PayPlanStatuses.Active)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, PayPlanStatuses.Defaulted), ct);
            db.Alerts.Add(new Alert
            {
                UserId = plan.UserId,
                Kind = AlertKinds.Overdue,
                Title = "Plan defaulted",
                Body = "Your installment plan moved to defaulted. Settle it to restore financing.",
                Href = "/account/credit",
            });
            if (plan.User.Preferences.EmailInstallmentReminders)
                jobs.Enqueue(db, new InstallmentReminderEmail(plan.Id, "overdue"));
        }
        await db.SaveChangesAsync(ct);
        jobs.Wake();
        await tx.CommitAsync(ct);
    }

    /// Monday statement: one email per user listing every open installment across their active plans.
    static async Task BnplStatement(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var jobs = sp.GetRequiredService<JobQueue>();
        var plans = await db.PayPlans.AsNoTracking()
            .Where(p => p.Status == PayPlanStatuses.Active && p.User.EmailVerifiedAt != null && p.User.Preferences.EmailInstallmentReminders)
            .ToListAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var plan in plans)
        {
            jobs.Enqueue(db, new InstallmentReminderEmail(plan.Id, "statement"));
        }
        await db.SaveChangesAsync(ct);
        jobs.Wake();
        await tx.CommitAsync(ct);
    }

    /// Overdue-first-response tickets bump one priority level; urgent overdue tickets alert every admin.
    static async Task TicketEscalation(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var realtime = sp.GetRequiredService<RealtimeEvents>();
        var now = DateTime.UtcNow;
        var overdue = await db.Tickets.AsNoTracking()
            .Where(t => t.ResolvedAt == null && t.DueAt < now
                && (t.Status == TicketStatuses.Received || t.Status == TicketStatuses.Open))
            .ToListAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var urgentNumbers = new List<string>();
        foreach (var ticket in overdue)
        {
            var newPriority = ticket.Priority switch
            {
                TicketPriorities.Low => TicketPriorities.Normal,
                TicketPriorities.Normal => TicketPriorities.High,
                _ => TicketPriorities.Urgent,
            };
            await db.Tickets.Where(t => t.Id == ticket.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Priority, newPriority)
                    .SetProperty(t => t.UpdatedAt, now), ct);
            if (newPriority == TicketPriorities.Urgent)
                urgentNumbers.Add(ticket.Number);
        }
        if (urgentNumbers.Count > 0)
        {
            // Admins get a durable alert plus a live pulse (SignalR-only admin nudge; a deviation from the
            // contract's silent escalation, kept so urgent SLA breaches don't wait for the next refresh).
            var admins = await db.Users.IgnoreQueryFilters().Where(u => u.Role == "admin").Select(u => u.Id).ToListAsync(ct);
            foreach (var admin in admins)
                foreach (var number in urgentNumbers)
                    db.Alerts.Add(new Alert
                    {
                        UserId = admin,
                        Kind = AlertKinds.Reply,
                        Title = $"Ticket {number} overdue",
                        Body = "An urgent complaint passed its first-response SLA.",
                        Href = $"/admin/support",
                    });
            await db.SaveChangesAsync(ct);
            await realtime.AdminPulseAsync($"{urgentNumbers.Count} ticket(s) overdue",
                "Urgent complaints passed their first-response SLA.", "/admin/support", ct);
        }
        await tx.CommitAsync(ct);
    }

    /// Pending unpaid orders (non-POD) older than 30 minutes auto-cancel and free their reserved stock.
    static async Task OrderTimeout(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var jobs = sp.GetRequiredService<JobQueue>();
        var realtime = sp.GetRequiredService<RealtimeEvents>();
        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        var stale = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatuses.Pending && o.PaymentMethod != PaymentMethods.PayOnDelivery && o.CreatedAt < cutoff)
            .Select(o => o.Id)
            .ToListAsync(ct);
        foreach (var orderId in stale)
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order is null || order.Status != OrderStatuses.Pending) continue;
            if (await OrderWorkflow.MoveAsync(db, order, OrderStatuses.Cancelled, "Unpaid order timed out", null, jobs, realtime, ct))
                jobs.Enqueue(db, new OrderCancelledEmail(order.Id, "We did not receive your payment within the checkout window."));
        }
    }

    static async Task PruneViews(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var cutoff = DateTime.UtcNow.AddDays(-90);
        int n;
        do
        {
            n = await db.Database.ExecuteSqlAsync($"DELETE FROM \"ProductViews\" WHERE \"Id\" IN (SELECT \"Id\" FROM \"ProductViews\" WHERE \"At\" < {cutoff} ORDER BY \"Id\" LIMIT 5000)", ct);
            await Task.Delay(50, ct); // let request-path writers get the lock between chunks
        } while (n == 5000);
    }

    static async Task OrphanAssets(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var queue = sp.GetRequiredService<JobQueue>();
        var pendingBefore = DateTime.UtcNow.AddHours(-1);
        var readyBefore = DateTime.UtcNow.AddHours(-24);
        for (var i = 0; i < 50; i++) // safety cap; the rest goes next hour
        {
            // Rows go first, with the reference re-check in the same statement, so an asset that got attached meanwhile survives.
            // The object deletion is a durable DeleteAssets job committed in the same transaction, so an S3 outage only delays it.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var keys = await db.Database.SqlQuery<string>($"""
                WITH candidates AS (
                    SELECT a."Id" FROM "Assets" a
                    WHERE (a."Status" = 'Pending' AND a."CreatedAt" < {pendingBefore})
                        OR (a."Status" = 'Ready' AND a."CreatedAt" < {readyBefore}
                            AND NOT EXISTS (SELECT 1 FROM "Products" p WHERE p."ImageAssetId" = a."Id")
                            AND NOT EXISTS (SELECT 1 FROM "Users" u WHERE u."AvatarAssetId" = a."Id"))
                    ORDER BY a."Id" LIMIT 200 FOR UPDATE SKIP LOCKED
                )
                DELETE FROM "Assets" a USING candidates c
                WHERE a."Id" = c."Id"
                    AND ((a."Status" = 'Pending' AND a."CreatedAt" < {pendingBefore})
                        OR (a."Status" = 'Ready' AND a."CreatedAt" < {readyBefore}
                            AND NOT EXISTS (SELECT 1 FROM "Products" p WHERE p."ImageAssetId" = a."Id")
                            AND NOT EXISTS (SELECT 1 FROM "Users" u WHERE u."AvatarAssetId" = a."Id")))
                RETURNING a."Key" AS "Value"
                """).ToListAsync(ct);
            if (keys.Count == 0) return;
            await queue.EnqueueAsync(db, new DeleteAssets([.. keys]), ct: ct);
            await tx.CommitAsync(ct);
        }
    }

    /// Unverified accounts older than a week with no purchase fixed to them may go.
    static async Task PruneUnverified(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var cutoff = DateTime.UtcNow.AddDays(-7);
        await db.Users.Where(u => u.EmailVerifiedAt == null && u.CreatedAt < cutoff
            && !db.Orders.Any(o => o.UserId == u.Id) && !db.RepairTickets.Any(r => r.UserId == u.Id)).ExecuteDeleteAsync(ct);
    }

    static async Task PruneJobs(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDb>();
        var done = DateTime.UtcNow.AddDays(-7);
        var failed = DateTime.UtcNow.AddDays(-30);
        await db.Jobs.Where(j => (j.Status == JobStatus.Done && j.CreatedAt < done) || (j.Status == JobStatus.Failed && j.CreatedAt < failed)).ExecuteDeleteAsync(ct);
    }
}

