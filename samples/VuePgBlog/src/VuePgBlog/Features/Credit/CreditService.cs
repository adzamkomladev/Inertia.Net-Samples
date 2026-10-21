using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Infrastructure;

namespace VuePgBlog.Features;

/// Customer BNPL dashboard: plans + installments, instant installment pay, plan guardrails.
public static partial class CreditService
{
    /// Filter token for the admin credit page ("overdue" is not itself a plan status).
    const string OverdueFilter = "overdue";

    internal static async Task<IResult> AccountAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var uid = ctx.User.UserId();
        return Render(
            "account/Credit",
            new InertiaProps
            {
                ["plans"] = await PlansWithRows(db, uid, ct),
                ["blocked"] = await IsBlockedAsync(db, uid, ct),
            }
        );
    }

    internal static async Task<List<PlanView>> PlansWithRows(AppDb db, Guid uid, CancellationToken ct) =>
        await db.PayPlans.AsNoTracking()
            .Where(p => p.UserId == uid)
            .OrderByDescending(p => p.StartedAt)
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
            .ToListAsync(ct);

    /// Plan rules: no NEW plan while overdue installments exist, and at most two active plans.
    internal static async Task<bool> IsBlockedAsync(AppDb db, Guid uid, CancellationToken ct) =>
        await db.Installments.AnyAsync(i => i.Plan.UserId == uid && i.Status == InstallmentStatuses.Overdue, ct)
        || await db.PayPlans.CountAsync(p => p.UserId == uid && p.Status == PayPlanStatuses.Active, ct) >= 2;

    internal static async Task<IResult> PayInstallmentAsync(Guid id, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, IHostEnvironment env, CancellationToken ct)
    {
        var uid = ctx.User.UserId();
        var installment = await db.Installments
            .Include(i => i.Plan)
            .ThenInclude(p => p.Order)
            .FirstOrDefaultAsync(i => i.Id == id && i.Plan.UserId == uid, ct);
        if (installment is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (installment.Status == InstallmentStatuses.Paid)
            return Back().WithFlash("toast", new Toast("info", "That installment is already paid."));
        if (installment.Status == InstallmentStatuses.Waived)
            return Back().WithFlash("toast", new Toast("info", "That installment was waived."));
        if (installment.Plan.Status is not PayPlanStatuses.Active)
            return Back().WithFlash("toast", new Toast("error", "That plan is not active."));

        var now = DateTime.UtcNow;
        installment.PaidAt = now;
        installment.Status = InstallmentStatuses.Paid;
        installment.PaymentRef = env.IsDevelopment() ? $"simulated-{now.Ticks}" : $"pay-{now.Ticks}";

        db.Alerts.Add(new Alert
        {
            UserId = uid,
            Kind = AlertKinds.InstallmentDue,
            Title = $"Installment {installment.Number} paid",
            Body = $"{Pricing.Show(installment.Amount)} for the plan on order {installment.Plan.Order?.Number}.",
            Href = "/account/credit",
            CreatedAt = now,
        });

        // Plan completes when every installment is settled.
        var remaining = await db.Installments.CountAsync(i => i.PlanId == installment.PlanId
            && i.Status != InstallmentStatuses.Paid && i.Status != InstallmentStatuses.Waived, ct);
        if (remaining == 0)
        {
            installment.Plan.Status = PayPlanStatuses.Completed;
            installment.Plan.CompletedAt = now;
        }

        jobs.Enqueue(db, new InstallmentPaidEmail(installment.Id));
        await db.SaveChangesAsync(ct);

        await realtime.NotifyUserAsync(uid, "Installment paid",
            $"Installment {installment.Number} of {installment.Plan.InstallmentCount} paid.", "/account/credit", ct);
        await realtime.OrderTimelineUpdatedAsync(uid, installment.Plan.Order?.Number ?? "", ct);
        ctx.Inertia().Flash("toast", new Toast("success", $"Installment {installment.Number} paid."));
        return Back();
    }

    // ---------------------------------------------------------------- admin

    internal static async Task<IResult> AdminAsync(string? status, int? page, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var pageNo = Math.Max(page ?? 1, 1);
        var size = 25;
        var query = db.PayPlans.AsNoTracking();
        if (status == OverdueFilter)
            query = query.Where(p => db.Installments.Any(i => i.PlanId == p.Id && i.Status == InstallmentStatuses.Overdue));
        else if (!string.IsNullOrWhiteSpace(status) && AdminStatuses.Contains(status))
            query = query.Where(p => p.Status == status);
        var rows = await query
            .OrderByDescending(p => p.StartedAt)
            .Skip((pageNo - 1) * size).Take(size + 1)
            .Select(p => new
            {
                p.Id,
                p.Status,
                p.InstallmentCount,
                p.AmountPerInstallment,
                p.FeeTotal,
                p.StartedAt,
                p.CompletedAt,
                OrderNumber = p.Order.Number,
                Customer = p.User.Name,
                Email = p.User.Email,
                Overdue = db.Installments.Count(i => i.PlanId == p.Id && i.Status == InstallmentStatuses.Overdue),
                Unpaid = db.Installments.Count(i => i.PlanId == p.Id && i.Status is InstallmentStatuses.Due or InstallmentStatuses.Overdue),
            })
            .ToListAsync(ct);
        return Render(
            "admin/Credit",
            new InertiaProps
            {
                ["plans"] = new AdminPlansPage(
                    rows.Take(size).Select(r => (object)r).ToList(),
                    pageNo,
                    rows.Count > size),
                ["statusFilter"] = status ?? "",
            }
        );
    }

    static readonly string[] AdminStatuses =
        [PayPlanStatuses.Active, PayPlanStatuses.Completed, PayPlanStatuses.Defaulted, PayPlanStatuses.Cancelled];
}

public sealed record AdminPlansPage(IReadOnlyList<object> Data, int Page, bool HasMore) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}
