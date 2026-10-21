using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class PayPlanStatuses
{
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Defaulted = "defaulted";
    public const string Cancelled = "cancelled";
}

public static class InstallmentStatuses
{
    public const string Due = "due";
    public const string Paid = "paid";
    public const string Overdue = "overdue";
    public const string Waived = "waived";
}

public sealed class PayPlan
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// One plan per order: BNPL covers the whole order or nothing.
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    /// 3, 6 or 12 installments, 14 days apart.
    public int InstallmentCount { get; set; }
    public decimal AmountPerInstallment { get; set; }
    /// Fee baked into the schedule: Pricing.BnplFeePercent of the order total.
    public decimal FeeTotal { get; set; }
    public string Status { get; set; } = PayPlanStatuses.Active;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? DemoKey { get; set; }
}

public sealed class Installment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PlanId { get; set; }
    public PayPlan Plan { get; set; } = null!;
    public int Number { get; set; }
    public decimal Amount { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? PaymentRef { get; set; }
    public string Status { get; set; } = InstallmentStatuses.Due;
}

public sealed record InstallmentRow(Guid Id, int Number, decimal Amount, DateOnly DueDate, string Status, DateTime? PaidAt);
public sealed record PlanView(Guid Id, int InstallmentCount, decimal AmountPerInstallment, decimal FeeTotal, string Status, DateTime StartedAt, DateTime? CompletedAt, List<InstallmentRow> Installments);
public sealed record CreditState(int PlanCount, bool Blocked);
public sealed record AdminPlanRow(
    Guid Id, string Status, int InstallmentCount, decimal AmountPerInstallment, decimal FeeTotal, DateTime StartedAt, DateTime? CompletedAt,
    string OrderNumber, string Customer, string Email, int Overdue, int Unpaid);

/// Customer BNPL dashboard: plans and installments, instant installment payment, plan guardrails.
public sealed class CreditService(AppDb db, JobQueue jobs, RealtimeEvents realtime, IHostEnvironment env)
{
    const int PageSize = 25;
    const string OverdueFilter = "overdue"; // not a plan status: plans that have an overdue installment

    static readonly string[] PlanFilters =
        [PayPlanStatuses.Active, PayPlanStatuses.Completed, PayPlanStatuses.Defaulted, PayPlanStatuses.Cancelled];

    public static IQueryable<PlanView> PlanViews(AppDb db, IQueryable<PayPlan> plans) =>
        plans.AsNoTracking().OrderByDescending(p => p.StartedAt).Select(p => new PlanView(
            p.Id, p.InstallmentCount, p.AmountPerInstallment, p.FeeTotal, p.Status, p.StartedAt, p.CompletedAt,
            db.Installments.Where(i => i.PlanId == p.Id).OrderBy(i => i.Number)
                .Select(i => new InstallmentRow(i.Id, i.Number, i.Amount, i.DueDate, i.Status, i.PaidAt)).ToList()));

    /// Active plan count, and whether an overdue installment (or one past its due date that the cron has not flagged yet) blocks a new plan.
    public Task<CreditState> StateAsync(Guid uid, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return db.OneRow.Select(_ => new CreditState(
                db.PayPlans.Count(p => p.UserId == uid && p.Status == PayPlanStatuses.Active),
                db.Installments.Any(i => i.Plan.UserId == uid
                    && (i.Status == InstallmentStatuses.Overdue || (i.Status == InstallmentStatuses.Due && i.DueDate < today)))))
            .FirstAsync(ct);
    }

    public async Task<(List<PlanView> Plans, bool Blocked)> AccountAsync(Guid uid, CancellationToken ct)
    {
        var plans = await PlanViews(db, db.PayPlans.Where(p => p.UserId == uid)).ToListAsync(ct);
        var state = await StateAsync(uid, ct);
        return (plans, state.Blocked || state.PlanCount >= 2);
    }

    public async Task<SaveResult> PayInstallmentAsync(Guid uid, Guid id, CancellationToken ct)
    {
        var installment = await db.Installments.Include(i => i.Plan).ThenInclude(p => p.Order)
            .FirstOrDefaultAsync(i => i.Id == id && i.Plan.UserId == uid, ct);
        if (installment is null) return SaveResult.Missing;
        if (installment.Status == InstallmentStatuses.Paid) return SaveResult.Info("That installment is already paid.");
        if (installment.Status == InstallmentStatuses.Waived) return SaveResult.Info("That installment was waived.");
        var plan = installment.Plan;
        if (plan.Status != PayPlanStatuses.Active) return SaveResult.Error("That plan is not active.");

        var now = DateTime.UtcNow;
        installment.PaidAt = now;
        installment.Status = InstallmentStatuses.Paid;
        installment.PaymentRef = $"{(env.IsDevelopment() ? "simulated" : "pay")}-{now.Ticks}";
        db.Alerts.Add(new Alert
        {
            UserId = uid,
            Kind = AlertKinds.InstallmentDue,
            Title = $"Installment {installment.Number} paid",
            Body = $"{Pricing.Show(installment.Amount)} for the plan on order {plan.Order.Number}.",
            Href = "/account/credit",
        });
        // The plan completes when every other installment is settled too.
        if (!await db.Installments.AnyAsync(i => i.PlanId == plan.Id && i.Id != id
                && i.Status != InstallmentStatuses.Paid && i.Status != InstallmentStatuses.Waived, ct))
        {
            plan.Status = PayPlanStatuses.Completed;
            plan.CompletedAt = now;
        }
        jobs.Enqueue(db, new InstallmentPaidEmail(id));
        await db.SaveChangesAsync(ct);
        jobs.Wake();

        await realtime.NotifyUserAsync(uid, "Installment paid", $"Installment {installment.Number} of {plan.InstallmentCount} paid.", "/account/credit", ct);
        await realtime.OrderTimelineUpdatedAsync(uid, plan.Order.Number, ct);
        return SaveResult.Success($"Installment {installment.Number} paid.");
    }

    public Task<Paged<AdminPlanRow>> AdminPlansAsync(string? status, int? page, CancellationToken ct)
    {
        var query = db.PayPlans.AsNoTracking();
        if (status == OverdueFilter)
            query = query.Where(p => db.Installments.Any(i => i.PlanId == p.Id && i.Status == InstallmentStatuses.Overdue));
        else if (status is not null && PlanFilters.Contains(status))
            query = query.Where(p => p.Status == status);
        return query
            .OrderByDescending(p => p.StartedAt)
            .Select(p => new AdminPlanRow(
                p.Id, p.Status, p.InstallmentCount, p.AmountPerInstallment, p.FeeTotal, p.StartedAt, p.CompletedAt,
                p.Order.Number, p.User.Name, p.User.Email,
                db.Installments.Count(i => i.PlanId == p.Id && i.Status == InstallmentStatuses.Overdue),
                db.Installments.Count(i => i.PlanId == p.Id && (i.Status == InstallmentStatuses.Due || i.Status == InstallmentStatuses.Overdue))))
            .ToPagedAsync(page, PageSize, ct);
    }
}
