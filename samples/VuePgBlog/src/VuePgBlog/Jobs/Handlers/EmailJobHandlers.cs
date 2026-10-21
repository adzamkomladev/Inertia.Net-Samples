using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Features;
using static VuePgBlog.Features.AuthHelpers;

namespace VuePgBlog.Infrastructure;

public sealed class SendEmailHandler(Mailer mailer) : IJobHandler<SendEmail>
{
    public Task HandleAsync(SendEmail m, CancellationToken ct) => mailer.SendAsync(m.To, m.Subject, m.Html, m.Text, ct);
}
public sealed class DeleteAssetsHandler(Storage storage) : IJobHandler<DeleteAssets>
{
    public Task HandleAsync(DeleteAssets m, CancellationToken ct) => storage.DeleteAsync(m.Keys, ct);
}

public sealed class WelcomeEmailHandler(AppDb db, Mailer mailer, IConfiguration config, Microsoft.AspNetCore.DataProtection.IDataProtectionProvider dp) : IJobHandler<WelcomeEmail>
{
    public async Task HandleAsync(WelcomeEmail m, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == m.UserId, ct);
        if (user is null) return;
        var base_ = config.AppBaseUrl() ?? "http://localhost:5199";
        // Unverified users get the verification link inlined; verified users land straight in the app.
        var url = user.EmailVerifiedAt is null
            ? $"{base_}/email/verify/confirm?token={Uri.EscapeDataString(Protector(dp).Protect($"{user.Id}:{user.Email}", TimeSpan.FromHours(24)))}"
            : base_;
        var mail = Emails.Welcome(user.Email, user.Name, url);
        await mailer.SendAsync(mail.To, mail.Subject, mail.Html, mail.Text, ct);
    }
}

/// Only meaningful outside the dev-simulated settle path (POD confirmations, manual admin confirms).

public sealed class OrderPaidEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : IJobHandler<OrderPaidEmail>
{
    public async Task HandleAsync(OrderPaidEmail m, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.User).Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == m.OrderId, ct);
        if (order is null) return;
        if (order.User.Role == "admin" || !order.User.Preferences.EmailOrderUpdates) return;
        var mail = Emails.OrderPaid(order.User.Email, order.User.Name, order.Number, order.ReceiptNumber ?? "—",
            order.Items.Select(i => (i.Name, i.Qty, i.LineTotal)).ToList(), order.Total,
            $"{(config.AppBaseUrl() ?? "http://localhost:5199")}/orders/{order.Number}?t={order.AccessToken}");
        await mailer.SendAsync(mail.To, mail.Subject, mail.Html, mail.Text, ct);
    }
}

public sealed class OrderShippedEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : IJobHandler<OrderShippedEmail>
{
    public async Task HandleAsync(OrderShippedEmail m, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.User)
            .Include(o => o.Items).ThenInclude(i => i.Product).ThenInclude(p => p != null ? p.Category : null)
            .FirstOrDefaultAsync(o => o.Id == m.OrderId, ct);
        if (order is null) return;
        if (order.User.Role == "admin" || !order.User.Preferences.EmailOrderUpdates) return;
        var rider = await db.Deliveries.AsNoTracking().Where(d => d.OrderId == order.Id)
            .Select(d => d.Rider != null ? d.Rider.Name : null).FirstOrDefaultAsync(ct);
        var zone = await db.Orders.AsNoTracking().Where(o => o.Id == order.Id)
            .Select(o => o.Zone != null ? o.Zone.EtaText : null).FirstOrDefaultAsync(ct);
        var mail = Emails.OrderShipped(order.User.Email, order.User.Name, order.Number, rider, zone ?? "1–2 days",
            $"{(config.AppBaseUrl() ?? "http://localhost:5199")}/orders/{order.Number}?t={order.AccessToken}");
        await mailer.SendAsync(mail.To, mail.Subject, mail.Html, mail.Text, ct);
    }
}

public sealed class OrderCancelledEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : IJobHandler<OrderCancelledEmail>
{
    public async Task HandleAsync(OrderCancelledEmail m, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.User).FirstOrDefaultAsync(o => o.Id == m.OrderId, ct);
        if (order is null) return;
        if (order.User.Role == "admin" || !order.User.Preferences.EmailOrderUpdates) return;
        var mail = Emails.OrderCancelled(order.User.Email, order.User.Name, order.Number, m.Reason,
            $"{(config.AppBaseUrl() ?? "http://localhost:5199")}/account/orders/{order.Number}");
        await mailer.SendAsync(mail.To, mail.Subject, mail.Html, mail.Text, ct);
    }
}

public sealed class OrderRefundEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : IJobHandler<OrderRefundEmail>
{
    public async Task HandleAsync(OrderRefundEmail m, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.User).FirstOrDefaultAsync(o => o.Id == m.OrderId, ct);
        if (order is null) return;
        if (order.User.Role == "admin" || !order.User.Preferences.EmailOrderUpdates) return;
        var mail = Emails.OrderRefund(order.User.Email, order.User.Name, order.Number, order.Total,
            $"{(config.AppBaseUrl() ?? "http://localhost:5199")}/account/orders/{order.Number}");
        await mailer.SendAsync(mail.To, mail.Subject, mail.Html, mail.Text, ct);
    }
}

public sealed class InstallmentPaidEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : IJobHandler<InstallmentPaidEmail>
{
    public async Task HandleAsync(InstallmentPaidEmail m, CancellationToken ct)
    {
        var installment = await db.Installments.AsNoTracking().Include(i => i.Plan).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(i => i.Id == m.InstallmentId, ct);
        if (installment is null) return;
        var plan = installment.Plan;
        var user = plan.User;
        if (user.Role == "admin") return;
        var remaining = await db.Installments.AsNoTracking().Where(i => i.PlanId == plan.Id && i.Status != InstallmentStatuses.Paid && i.Id != installment.Id)
            .SumAsync(i => (decimal?)i.Amount, ct) ?? 0m;
        var mail = Emails.InstallmentPaid(user.Email, user.Name, plan.Id.ToString("N")[..8],
            plan.Order != null ? plan.Order.Number : "your plan", installment.Number, plan.InstallmentCount,
            installment.Amount, remaining, $"{(config.AppBaseUrl() ?? "http://localhost:5199")}/account/credit");
        await mailer.SendAsync(mail.To, mail.Subject, mail.Html, mail.Text, ct);
    }
}

/// Kind: reminder (due ≤3 days) | overdue | statement — sent by the BNPL crons.
public sealed class InstallmentReminderEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : IJobHandler<InstallmentReminderEmail>
{
    public async Task HandleAsync(InstallmentReminderEmail m, CancellationToken ct)
    {
        var plan = await db.PayPlans.AsNoTracking().Include(p => p.User).Include(p => p.Order).FirstOrDefaultAsync(p => p.Id == m.PlanId, ct);
        if (plan is null) return;
        var user = plan.User;
        if (user.Role == "admin" || !user.Preferences.EmailInstallmentReminders) return;
        var url = $"{(config.AppBaseUrl() ?? "http://localhost:5199")}/account/credit";
        var mail = m.Kind switch
        {
            "overdue" => (await db.Installments.AsNoTracking().FirstOrDefaultAsync(i => i.PlanId == plan.Id && i.Status == InstallmentStatuses.Overdue, ct)) is { } late
                ? Emails.InstallmentOverdue(user.Email, user.Name, plan.Order!.Number, late.Number, late.Amount, late.DueDate, url)
                : null,
            "statement" => Emails.InstallmentStatement(user.Email, user.Name,
                await db.Installments.AsNoTracking().Where(i => i.PlanId == plan.Id && i.Status != InstallmentStatuses.Paid)
                    .OrderBy(i => i.DueDate)
                    .Select(i => new ValueTuple<string, int, decimal, DateOnly, string>(plan.Order != null ? plan.Order.Number : "—", i.Number, i.Amount, i.DueDate, i.Status))
                    .ToListAsync(ct), url),
            _ => (await db.Installments.AsNoTracking().FirstOrDefaultAsync(i => i.PlanId == plan.Id && i.Status == InstallmentStatuses.Due, ct)) is { } due
                ? Emails.InstallmentReminder(user.Email, user.Name, plan.Order!.Number, due.Number, due.Amount, due.DueDate, url)
                : null,
        };
        if (mail is { } value) await mailer.SendAsync(value.To, value.Subject, value.Html, value.Text, ct);
    }
}

public sealed class TicketReplyEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : IJobHandler<TicketReplyEmail>
{
    public async Task HandleAsync(TicketReplyEmail m, CancellationToken ct)
    {
        var ticket = await db.Tickets.AsNoTracking().Include(t => t.User).FirstOrDefaultAsync(t => t.Id == m.TicketId, ct);
        if (ticket is null) return;
        // Guests have no inbox page: staff replies mail the thread straight to them.
        var mail = Emails.TicketReply(ticket.User?.Email ?? ticket.GuestEmail ?? "", ticket.User?.Name, ticket.Number, ticket.Subject,
            $"{(config.AppBaseUrl() ?? "http://localhost:5199")}/account/support");
        if (string.IsNullOrEmpty(mail.To)) return;
        if (ticket.User is { } user && (user.Role == "admin" || !user.Preferences.EmailSupportReplies)) return;
        await mailer.SendAsync(mail.To, mail.Subject, mail.Html, mail.Text, ct);
    }
}

/// Kind from CareService: warranty-claim | insurance-claim | repair.
public sealed class CareDecisionEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : IJobHandler<CareDecisionEmail>
{
    public async Task HandleAsync(CareDecisionEmail m, CancellationToken ct)
    {
        var url = $"{(config.AppBaseUrl() ?? "http://localhost:5199")}/account/care";
        SendEmail? mail = m.Kind switch
        {
            "warranty-claim" => await WarrantyAsync(m.Id, url, ct),
            "insurance-claim" => await InsuranceAsync(m.Id, url, ct),
            "repair" => await RepairAsync(m.Id, url, ct),
            _ => null,
        };
        if (mail is { } value) await mailer.SendAsync(value.To, value.Subject, value.Html, value.Text, ct);
    }

    async Task<SendEmail?> WarrantyAsync(Guid claimId, string url, CancellationToken ct)
    {
        var claim = await db.WarrantyClaims.AsNoTracking()
            .Include(c => c.Warranty).ThenInclude(w => w.User)
            .Include(c => c.Warranty).ThenInclude(w => w.OrderItem)
            .FirstOrDefaultAsync(c => c.Id == claimId, ct);
        if (claim is null) return null;
        var user = claim.Warranty.User;
        if (user.Role == "admin" || !user.Preferences.EmailOrderUpdates) return null;
        var decision = claim.Status switch
        {
            WarrantyClaimStatuses.InReview => "is under review",
            WarrantyClaimStatuses.Approved => "approved — replacement coming",
            WarrantyClaimStatuses.ReplacementShipped => "replacement shipped",
            WarrantyClaimStatuses.Rejected => "rejected",
            WarrantyClaimStatuses.Resolved => "resolved",
            _ => claim.Status,
        };
        return Emails.CareDecision(user.Email, user.Name, "warranty claim", claim.Warranty.OrderItem?.Name ?? "your device", decision, claim.DecisionNote, url);
    }

    async Task<SendEmail?> InsuranceAsync(Guid claimId, string url, CancellationToken ct)
    {
        var claim = await db.InsuranceClaims.AsNoTracking().Include(c => c.Policy).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(c => c.Id == claimId, ct);
        if (claim is null) return null;
        var user = claim.Policy.User;
        if (user.Role == "admin" || !user.Preferences.EmailOrderUpdates) return null;
        var decision = claim.Status switch
        {
            InsuranceClaimStatuses.UnderReview => "is under review",
            InsuranceClaimStatuses.Approved => $"approved — payout {Pricing.Show(claim.PayoutAmount ?? 0)}",
            InsuranceClaimStatuses.Paid => $"paid — {Pricing.Show(claim.PayoutAmount ?? 0)}",
            InsuranceClaimStatuses.Rejected => "rejected",
            _ => claim.Status,
        };
        return Emails.CareDecision(user.Email, user.Name, "insurance claim", claim.Policy.DeviceName, decision, null, url);
    }

    async Task<SendEmail?> RepairAsync(Guid ticketId, string url, CancellationToken ct)
    {
        var ticket = await db.RepairTickets.AsNoTracking().Include(t => t.User).FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        if (ticket is null) return null;
        var decision = $"{ticket.Status.Replace('_', ' ')}"
            + (ticket.QuotedCost is { } cost ? $" — quote {Pricing.Show(cost)}" : "");
        return Emails.CareDecision(ticket.User.Email, ticket.User.Name, "repair", ticket.DeviceName, decision, null, url);
    }
}
