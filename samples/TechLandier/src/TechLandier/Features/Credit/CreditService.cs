using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// Customer BNPL dashboard: plans + installments, instant installment pay, plan guardrails.
/// Returns plain data + SaveResult outcomes; the Credit.cs route handlers compose the Inertia responses.
public static partial class CreditService
{
    /// Filter token for the admin credit page ("overdue" is not itself a plan status).
    const string OverdueFilter = "overdue";

    internal static async Task<(List<PlanView> Plans, bool Blocked)> GetAccountAsync(Guid uid, AppDb db, CancellationToken ct) =>
        (await PlansWithRows(db, uid, ct), await IsBlockedAsync(db, uid, ct));

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

    internal static async Task<SaveResult> PayInstallmentAsync(Guid uid, Guid id, AppDb db, JobQueue jobs, RealtimeEvents realtime, IHostEnvironment env, CancellationToken ct)
    {
        var installment = await db.Installments
            .Include(i => i.Plan)
            .ThenInclude(p => p.Order)
            .FirstOrDefaultAsync(i => i.Id == id && i.Plan.UserId == uid, ct);
        if (installment is null)
            return new SaveResult(NotFound: true);
        if (installment.Status == InstallmentStatuses.Paid)
            return new SaveResult(FlashType: "info", FlashMessage: "That installment is already paid.");
        if (installment.Status == InstallmentStatuses.Waived)
            return new SaveResult(FlashType: "info", FlashMessage: "That installment was waived.");
        if (installment.Plan.Status is not PayPlanStatuses.Active)
            return new SaveResult(FlashType: "error", FlashMessage: "That plan is not active.");

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
        return new SaveResult(FlashType: "success", FlashMessage: $"Installment {installment.Number} paid.");
    }

    // ---------------------------------------------------------------- admin

    internal static async Task<(AdminPlansPage Plans, string StatusFilter)> GetAdminPlansAsync(
        string? status, int? page, AppDb db, CancellationToken ct)
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
                Unpaid = db.Installments.Count(i => i.PlanId == p.Id && i.Status == InstallmentStatuses.Due || i.Status == InstallmentStatuses.Overdue),
            })
            .ToListAsync(ct);
        return (new AdminPlansPage(rows.Take(size).Select(r => (object)r).ToList(), pageNo, rows.Count > size), status ?? "");
    }

    static readonly string[] AdminStatuses =
        [PayPlanStatuses.Active, PayPlanStatuses.Completed, PayPlanStatuses.Defaulted, PayPlanStatuses.Cancelled];
}

public sealed record AdminPlansPage(IReadOnlyList<object> Data, int Page, bool HasMore) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}
