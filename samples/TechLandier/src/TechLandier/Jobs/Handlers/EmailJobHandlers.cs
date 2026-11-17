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
public sealed class SendVerificationEmailHandler(AppDb db, Mailer mailer, EmailContexts contexts, IConfiguration config, IDataProtectionProvider dp) : global::Runwick.IJobHandler<SendVerificationEmail>
{
    public async Task HandleAsync(SendVerificationEmail m, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == m.UserId, ct);
        if (user is null || user.EmailVerifiedAt is not null) return;
        var mail = Emails.Verify(await contexts.CreateAsync(ct), user.Email, user.Name, AuthUtils.VerifyUrl(dp, config.PublicBaseUrl(), user));
        await mailer.SendAsync(mail, ct);
    }
}

public sealed class OrderPaidEmailHandler(AppDb db, Mailer mailer, EmailContexts contexts, DocumentsService docs) : global::Runwick.IJobHandler<OrderPaidEmail>
{
    public async Task HandleAsync(OrderPaidEmail m, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.User).FirstOrDefaultAsync(o => o.Id == m.OrderId, ct);
        if (order is null) return;
        if (order.User.Role == "admin" || !order.User.Preferences.EmailOrderUpdates) return;
        // No receipt data means the order is not (or no longer) paid; there is nothing to confirm.
        if (await docs.ReceiptByIdAsync(order.Id, ct) is not { } receipt) return;
        var c = await contexts.CreateAsync(ct);
        var mail = Emails.OrderPaid(c, order.User.Email, order.User.Name, receipt,
            c.Url($"/orders/{order.Number}?t={order.AccessToken}"),
            // The guest link works without signing in, like the tracking page.
            c.Url($"/orders/{order.Number}/receipt.pdf?t={Uri.EscapeDataString(order.AccessToken)}"));
        await mailer.SendAsync(mail, ct);
    }
}

public sealed class OrderShippedEmailHandler(AppDb db, Mailer mailer, EmailContexts contexts) : global::Runwick.IJobHandler<OrderShippedEmail>
{
    public async Task HandleAsync(OrderShippedEmail m, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.User).Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == m.OrderId, ct);
        if (order is null) return;
        if (order.User.Role == "admin" || !order.User.Preferences.EmailOrderUpdates) return;
        var rider = await db.Deliveries.AsNoTracking().Where(d => d.OrderId == order.Id)
            .Select(d => d.Rider != null ? d.Rider.Name : null).FirstOrDefaultAsync(ct);
        var zone = await db.Orders.AsNoTracking().Where(o => o.Id == order.Id)
            .Select(o => o.Zone != null ? o.Zone.EtaText : null).FirstOrDefaultAsync(ct);
        var c = await contexts.CreateAsync(ct);
        var mail = Emails.OrderShipped(c, order.User.Email, order.User.Name, order.Number,
            order.Items.Select(i => (i.Name, i.Qty)).ToList(), rider, zone ?? "1–2 days", c.Url($"/orders/{order.Number}?t={order.AccessToken}"));
        await mailer.SendAsync(mail, ct);
    }
}

public sealed class OrderCancelledEmailHandler(AppDb db, Mailer mailer, EmailContexts contexts) : global::Runwick.IJobHandler<OrderCancelledEmail>
{
    public async Task HandleAsync(OrderCancelledEmail m, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.User).FirstOrDefaultAsync(o => o.Id == m.OrderId, ct);
        if (order is null) return;
        if (order.User.Role == "admin" || !order.User.Preferences.EmailOrderUpdates) return;
        var c = await contexts.CreateAsync(ct);
        var mail = Emails.OrderCancelled(c, order.User.Email, order.User.Name, order.Number, m.Reason, c.Url($"/account/orders/{order.Number}"));
        await mailer.SendAsync(mail, ct);
    }
}

public sealed class OrderRefundEmailHandler(AppDb db, Mailer mailer, EmailContexts contexts) : global::Runwick.IJobHandler<OrderRefundEmail>
{
    public async Task HandleAsync(OrderRefundEmail m, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.User).FirstOrDefaultAsync(o => o.Id == m.OrderId, ct);
        if (order is null) return;
        if (order.User.Role == "admin" || !order.User.Preferences.EmailOrderUpdates) return;
        var c = await contexts.CreateAsync(ct);
        var mail = Emails.OrderRefund(c, order.User.Email, order.User.Name, order.Number, order.Total, c.Url($"/account/orders/{order.Number}"));
        await mailer.SendAsync(mail, ct);
    }
}

public sealed class InstallmentPaidEmailHandler(AppDb db, Mailer mailer, EmailContexts contexts) : global::Runwick.IJobHandler<InstallmentPaidEmail>
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
        var paidCount = await db.Installments.AsNoTracking().CountAsync(i => i.PlanId == plan.Id && i.Status == InstallmentStatuses.Paid, ct);
        var c = await contexts.CreateAsync(ct, user.Id);
        var mail = Emails.InstallmentPaid(c, user.Email, user.Name, plan.Order.Number, installment.Number, plan.InstallmentCount, paidCount,
            installment.Amount, remaining, c.Url("/account/credit"), c.Url($"/account/installments/{plan.Id}/statement.pdf"));
        await mailer.SendAsync(mail, ct);
    }
}

/// Kind: reminder (due within 3 days) | overdue, sent by the BNPL crons.
public sealed class InstallmentReminderEmailHandler(AppDb db, Mailer mailer, EmailContexts contexts) : global::Runwick.IJobHandler<InstallmentReminderEmail>
{
    public async Task HandleAsync(InstallmentReminderEmail m, CancellationToken ct)
    {
        var plan = await db.PayPlans.AsNoTracking().Include(p => p.User).Include(p => p.Order).FirstOrDefaultAsync(p => p.Id == m.PlanId, ct);
        if (plan is null) return;
        var user = plan.User;
        if (user.Role == "admin" || !user.Preferences.EmailInstallmentReminders) return;
        var c = await contexts.CreateAsync(ct, user.Id);
        var url = c.Url("/account/credit");
        var mail = m.Kind == "overdue"
            ? (await db.Installments.AsNoTracking().OrderBy(i => i.DueDate).FirstOrDefaultAsync(i => i.PlanId == plan.Id && i.Status == InstallmentStatuses.Overdue, ct)) is { } late
                ? Emails.InstallmentOverdue(c, user.Email, user.Name, plan.Order.Number, late.Number, late.Amount, late.DueDate, url)
                : null
            : (await db.Installments.AsNoTracking().OrderBy(i => i.DueDate).FirstOrDefaultAsync(i => i.PlanId == plan.Id && i.Status == InstallmentStatuses.Due, ct)) is { } due
                ? Emails.InstallmentReminder(c, user.Email, user.Name, plan.Order.Number, due.Number, due.Amount, due.DueDate, url)
                : null;
        if (mail is { } value) await mailer.SendAsync(value, ct);
    }
}

/// One statement per user: every active plan that still has open (not paid, not waived) installments.
public sealed class InstallmentStatementEmailHandler(AppDb db, Mailer mailer, EmailContexts contexts, DocumentsService docs) : global::Runwick.IJobHandler<InstallmentStatementEmail>
{
    public async Task HandleAsync(InstallmentStatementEmail m, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == m.UserId, ct);
        if (user is null || user.Role == "admin" || !user.Preferences.EmailInstallmentReminders) return;
        var planIds = await db.PayPlans.AsNoTracking()
            .Where(p => p.UserId == user.Id && p.Status == PayPlanStatuses.Active
                && db.Installments.Any(i => i.PlanId == p.Id && i.Status != InstallmentStatuses.Paid && i.Status != InstallmentStatuses.Waived))
            .OrderBy(p => p.StartedAt).Select(p => p.Id).ToListAsync(ct);
        var plans = new List<StatementData>();
        foreach (var id in planIds)
            if (await docs.StatementForEmailAsync(id, ct) is { } statement) plans.Add(statement);
        if (plans.Count == 0) return;
        var c = await contexts.CreateAsync(ct, user.Id);
        var mail = Emails.InstallmentStatement(c, user.Email, user.Name, plans, c.Url("/account/credit"));
        await mailer.SendAsync(mail, ct);
    }
}

public sealed class TicketReplyEmailHandler(AppDb db, Mailer mailer, EmailContexts contexts) : global::Runwick.IJobHandler<TicketReplyEmail>
{
    public async Task HandleAsync(TicketReplyEmail m, CancellationToken ct)
    {
        var ticket = await db.Tickets.AsNoTracking().Include(t => t.User).FirstOrDefaultAsync(t => t.Id == m.TicketId, ct);
        if (ticket is null) return;
        if (ticket.User is { } user && (user.Role == "admin" || !user.Preferences.EmailSupportReplies)) return;
        var to = ticket.User?.Email ?? ticket.GuestEmail ?? "";
        if (string.IsNullOrEmpty(to)) return;
        var reply = await db.TicketMessages.AsNoTracking().Where(x => x.TicketId == ticket.Id && x.IsStaff)
            .OrderByDescending(x => x.At).Select(x => x.Body).FirstOrDefaultAsync(ct);
        if (reply is null) return;
        var c = await contexts.CreateAsync(ct);
        // Guests cannot open /account/support (it needs a verified sign-in); they get the public support page instead.
        var url = ticket.User is null ? c.Url("/support/policy") : c.Url("/account/support");
        var mail = Emails.TicketReply(c, to, ticket.User?.Name, ticket.Number, ticket.Subject, reply, url);
        await mailer.SendAsync(mail, ct);
    }
}

/// Kind from CareService: warranty-claim | insurance-claim | repair.
public sealed class CareDecisionEmailHandler(AppDb db, Mailer mailer, EmailContexts contexts) : global::Runwick.IJobHandler<CareDecisionEmail>
{
    public async Task HandleAsync(CareDecisionEmail m, CancellationToken ct)
    {
        var c = await contexts.CreateAsync(ct);
        var url = c.Url("/account/care");
        SendEmail? mail = m.Kind switch
        {
            "warranty-claim" => await WarrantyAsync(c, m.Id, url, ct),
            "insurance-claim" => await InsuranceAsync(c, m.Id, url, ct),
            "repair" => await RepairAsync(c, m.Id, url, ct),
            _ => null,
        };
        if (mail is { } value) await mailer.SendAsync(value, ct);
    }

    async Task<SendEmail?> WarrantyAsync(EmailContext c, Guid claimId, string url, CancellationToken ct)
    {
        var claim = await db.WarrantyClaims.AsNoTracking()
            .Include(x => x.Warranty).ThenInclude(w => w.User)
            .Include(x => x.Warranty).ThenInclude(w => w.OrderItem)
            .FirstOrDefaultAsync(x => x.Id == claimId, ct);
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
        return Emails.CareDecision(c, user.Email, user.Name, "warranty claim", claim.Warranty.OrderItem?.Name ?? "your device", decision, claim.DecisionNote, url);
    }

    async Task<SendEmail?> InsuranceAsync(EmailContext c, Guid claimId, string url, CancellationToken ct)
    {
        var claim = await db.InsuranceClaims.AsNoTracking().Include(x => x.Policy).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(x => x.Id == claimId, ct);
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
        return Emails.CareDecision(c, user.Email, user.Name, "insurance claim", claim.Policy.DeviceName, decision, null, url);
    }

    async Task<SendEmail?> RepairAsync(EmailContext c, Guid ticketId, string url, CancellationToken ct)
    {
        var ticket = await db.RepairTickets.AsNoTracking().Include(t => t.User).FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        if (ticket is null) return null;
        var decision = $"{ticket.Status.Replace('_', ' ')}"
            + (ticket.QuotedCost is { } cost ? $" — quote {Pricing.Show(cost)}" : "");
        return Emails.CareDecision(c, ticket.User.Email, ticket.User.Name, "repair", ticket.DeviceName, decision, null, url);
    }
}
