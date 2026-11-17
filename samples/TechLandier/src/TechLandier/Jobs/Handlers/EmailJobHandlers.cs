using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Features;

namespace TechLandier.Infrastructure;

public sealed class DeleteAssetsHandler(Storage storage) : global::Runwick.IJobHandler<DeleteAssets>
{
    public async Task HandleAsync(DeleteAssets m, CancellationToken ct) => await storage.DeleteAsync(m.Keys, ct);
}

/// The token is minted here, not at enqueue time: a retry after a long outage still mails a link that is valid for 24 hours.
public sealed class SendVerificationEmailHandler(AppDb db, Mailer mailer, IConfiguration config, IDataProtectionProvider dp) : global::Runwick.IJobHandler<SendVerificationEmail>
{
    public async Task HandleAsync(SendVerificationEmail m, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == m.UserId, ct);
        if (user is null || user.EmailVerifiedAt is not null) return;
        var mail = Emails.Verify(user.Email, user.Name, AuthUtils.VerifyUrl(dp, config.PublicBaseUrl(), user));
        await mailer.SendAsync(mail.To, mail.Subject, mail.Html, mail.Text, ct);
    }
}
public sealed class OrderPaidEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : global::Runwick.IJobHandler<OrderPaidEmail>
{
    public async Task HandleAsync(OrderPaidEmail m, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.User).Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == m.OrderId, ct);
        if (order is null) return;
        if (order.User.Role == "admin" || !order.User.Preferences.EmailOrderUpdates) return;
        var mail = Emails.OrderPaid(order.User.Email, order.User.Name, order.Number, order.ReceiptNumber ?? "—",
            order.Items.Select(i => (i.Name, i.Qty, i.LineTotal)).ToList(), order.Total,
            $"{config.PublicBaseUrl()}/orders/{order.Number}?t={order.AccessToken}");
        await mailer.SendAsync(mail.To, mail.Subject, mail.Html, mail.Text, ct);
    }
}

public sealed class OrderShippedEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : global::Runwick.IJobHandler<OrderShippedEmail>
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
            $"{config.PublicBaseUrl()}/orders/{order.Number}?t={order.AccessToken}");
        await mailer.SendAsync(mail.To, mail.Subject, mail.Html, mail.Text, ct);
    }
}

public sealed class OrderCancelledEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : global::Runwick.IJobHandler<OrderCancelledEmail>
{
    public async Task HandleAsync(OrderCancelledEmail m, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.User).FirstOrDefaultAsync(o => o.Id == m.OrderId, ct);
        if (order is null) return;
        if (order.User.Role == "admin" || !order.User.Preferences.EmailOrderUpdates) return;
        var mail = Emails.OrderCancelled(order.User.Email, order.User.Name, order.Number, m.Reason,
            $"{config.PublicBaseUrl()}/account/orders/{order.Number}");
        await mailer.SendAsync(mail.To, mail.Subject, mail.Html, mail.Text, ct);
    }
}

public sealed class OrderRefundEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : global::Runwick.IJobHandler<OrderRefundEmail>
{
    public async Task HandleAsync(OrderRefundEmail m, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.User).FirstOrDefaultAsync(o => o.Id == m.OrderId, ct);
        if (order is null) return;
        if (order.User.Role == "admin" || !order.User.Preferences.EmailOrderUpdates) return;
        var mail = Emails.OrderRefund(order.User.Email, order.User.Name, order.Number, order.Total,
            $"{config.PublicBaseUrl()}/account/orders/{order.Number}");
        await mailer.SendAsync(mail.To, mail.Subject, mail.Html, mail.Text, ct);
    }
}

public sealed class InstallmentPaidEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : global::Runwick.IJobHandler<InstallmentPaidEmail>
{
    public async Task HandleAsync(InstallmentPaidEmail m, CancellationToken ct)
    {
        var installment = await db.Installments.AsNoTracking()
            .Include(i => i.Plan).ThenInclude(p => p.User).Include(i => i.Plan).ThenInclude(p => p.Order)
            .FirstOrDefaultAsync(i => i.Id == m.InstallmentId, ct);
        if (installment is null) return;
        var plan = installment.Plan;
        var user = plan.User;
        if (user.Role == "admin" || !user.Preferences.EmailInstallmentReminders) return;
        // Waived installments are not owed, so they do not count toward what is left.
        var remaining = await db.Installments.AsNoTracking()
            .Where(i => i.PlanId == plan.Id && i.Id != installment.Id && i.Status != InstallmentStatuses.Paid && i.Status != InstallmentStatuses.Waived)
            .SumAsync(i => (decimal?)i.Amount, ct) ?? 0m;
        var mail = Emails.InstallmentPaid(user.Email, user.Name, plan.Order.Number, installment.Number, plan.InstallmentCount,
            installment.Amount, remaining, $"{config.PublicBaseUrl()}/account/credit");
        await mailer.SendAsync(mail.To, mail.Subject, mail.Html, mail.Text, ct);
    }
}

/// Kind: reminder (due within 3 days) | overdue, sent by the BNPL crons.
public sealed class InstallmentReminderEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : global::Runwick.IJobHandler<InstallmentReminderEmail>
{
    public async Task HandleAsync(InstallmentReminderEmail m, CancellationToken ct)
    {
        var plan = await db.PayPlans.AsNoTracking().Include(p => p.User).Include(p => p.Order).FirstOrDefaultAsync(p => p.Id == m.PlanId, ct);
        if (plan is null) return;
        var user = plan.User;
        if (user.Role == "admin" || !user.Preferences.EmailInstallmentReminders) return;
        var url = $"{config.PublicBaseUrl()}/account/credit";
        var mail = m.Kind == "overdue"
            ? (await db.Installments.AsNoTracking().OrderBy(i => i.DueDate).FirstOrDefaultAsync(i => i.PlanId == plan.Id && i.Status == InstallmentStatuses.Overdue, ct)) is { } late
                ? Emails.InstallmentOverdue(user.Email, user.Name, plan.Order.Number, late.Number, late.Amount, late.DueDate, url)
                : null
            : (await db.Installments.AsNoTracking().OrderBy(i => i.DueDate).FirstOrDefaultAsync(i => i.PlanId == plan.Id && i.Status == InstallmentStatuses.Due, ct)) is { } due
                ? Emails.InstallmentReminder(user.Email, user.Name, plan.Order.Number, due.Number, due.Amount, due.DueDate, url)
                : null;
        if (mail is { } value) await mailer.SendAsync(value.To, value.Subject, value.Html, value.Text, ct);
    }
}

/// One statement per user: every open (not paid, not waived) installment across their active plans.
public sealed class InstallmentStatementEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : global::Runwick.IJobHandler<InstallmentStatementEmail>
{
    public async Task HandleAsync(InstallmentStatementEmail m, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == m.UserId, ct);
        if (user is null || user.Role == "admin" || !user.Preferences.EmailInstallmentReminders) return;
        var rows = await db.Installments.AsNoTracking()
            .Where(i => i.Plan.UserId == user.Id && i.Plan.Status == PayPlanStatuses.Active
                && i.Status != InstallmentStatuses.Paid && i.Status != InstallmentStatuses.Waived)
            .OrderBy(i => i.DueDate)
            .Select(i => new ValueTuple<string, int, decimal, DateOnly, string>(i.Plan.Order.Number, i.Number, i.Amount, i.DueDate, i.Status))
            .ToListAsync(ct);
        if (rows.Count == 0) return;
        var mail = Emails.InstallmentStatement(user.Email, user.Name, rows, $"{config.PublicBaseUrl()}/account/credit");
        await mailer.SendAsync(mail.To, mail.Subject, mail.Html, mail.Text, ct);
    }
}

public sealed class TicketReplyEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : global::Runwick.IJobHandler<TicketReplyEmail>
{
    public async Task HandleAsync(TicketReplyEmail m, CancellationToken ct)
    {
        var ticket = await db.Tickets.AsNoTracking().Include(t => t.User).FirstOrDefaultAsync(t => t.Id == m.TicketId, ct);
        if (ticket is null) return;
        if (ticket.User is { } user && (user.Role == "admin" || !user.Preferences.EmailSupportReplies)) return;
        // Guests cannot open /account/support (it needs a verified sign-in); they get the public support page instead.
        var url = ticket.User is null ? $"{config.PublicBaseUrl()}/support/policy" : $"{config.PublicBaseUrl()}/account/support";
        var mail = Emails.TicketReply(ticket.User?.Email ?? ticket.GuestEmail ?? "", ticket.User?.Name, ticket.Number, ticket.Subject, url);
        if (string.IsNullOrEmpty(mail.To)) return;
        await mailer.SendAsync(mail.To, mail.Subject, mail.Html, mail.Text, ct);
    }
}
/// Kind from CareService: warranty-claim | insurance-claim | repair.
public sealed class CareDecisionEmailHandler(AppDb db, Mailer mailer, IConfiguration config) : global::Runwick.IJobHandler<CareDecisionEmail>
{
    public async Task HandleAsync(CareDecisionEmail m, CancellationToken ct)
    {
        var url = $"{config.PublicBaseUrl()}/account/care";
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
