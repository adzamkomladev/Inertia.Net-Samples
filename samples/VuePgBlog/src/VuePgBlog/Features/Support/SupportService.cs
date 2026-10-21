using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Infrastructure;

namespace VuePgBlog.Features;

/// Complaints: public guest form, customer tickets, admin inbox. Human numbers T-0001, SLA by priority.
/// Returns plain data + SaveResult outcomes; the Support.cs route handlers compose the Inertia responses.
public static partial class SupportService
{
    internal static readonly string[] CategoryList =
    [
        TicketCategories.ProductFault, TicketCategories.WrongItem, TicketCategories.LateDelivery,
        TicketCategories.DeliveryIssue, TicketCategories.Refund, TicketCategories.Warranty,
        TicketCategories.Insurance, TicketCategories.Other,
    ];

    // ---------------------------------------------------------------- account

    internal static async Task<object> GetTicketsAsync(Guid uid, AppDb db, CancellationToken ct)
    {
        return await db.Tickets.AsNoTracking()
            .Where(t => t.UserId == uid)
            .OrderByDescending(t => t.UpdatedAt).ThenByDescending(t => t.Id)
            .Take(100)
            .Select(t => new
            {
                t.Id,
                t.Number,
                t.Subject,
                t.Category,
                t.Status,
                t.Priority,
                t.DueAt,
                t.ResolvedAt,
                t.CreatedAt,
                OrderNumber = t.Order != null ? t.Order.Number : null,
                Messages = db.TicketMessages.Where(m => m.TicketId == t.Id)
                    .OrderBy(m => m.At).ThenBy(m => m.Id)
                    .Select(m => new { m.Id, m.IsStaff, m.Body, m.At })
                    .ToList(),
            })
            .ToListAsync(ct);
    }

    internal static async Task<SaveResult> SaveTicketAsync(
        Guid uid, string? subject, string? category, string? message, string? orderNumber,
        AppDb db, CancellationToken ct)
    {
        subject = subject?.Trim();
        if (string.IsNullOrWhiteSpace(subject) || subject.Length < 4)
            return new SaveResult(Errors: new Dictionary<string, string> { ["subject"] = "Give the thread a subject (4+ characters)." });
        if (string.IsNullOrWhiteSpace(message))
            return new SaveResult(Errors: new Dictionary<string, string> { ["message"] = "Describe the problem." });
        var cat = CategoryList.Contains(category) ? category! : TicketCategories.Other;

        Guid? orderId = null;
        if (!string.IsNullOrWhiteSpace(orderNumber))
            orderId = await db.Orders.AsNoTracking()
                .Where(o => o.Number == orderNumber.Trim() && o.UserId == uid)
                .Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct);

        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await OrderNumbers.NextTicketAsync(db, ct);
        var ticket = new Ticket
        {
            Number = number,
            UserId = uid,
            OrderId = orderId,
            Subject = subject,
            Category = cat,
            Status = TicketStatuses.Received,
            Priority = TicketPriorities.Normal,
            DueAt = now.AddHours(TicketSla.Hours(TicketPriorities.Normal)),
            LastMessageAt = now,
        };
        db.Tickets.Add(ticket);
        db.TicketMessages.Add(new TicketMessage
        {
            Ticket = ticket,
            AuthorUserId = uid,
            IsStaff = false,
            Body = message!.Trim(),
            At = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new SaveResult(FlashType: "success", FlashMessage: $"Ticket {number} opened — support replies within {TicketSla.Hours(TicketPriorities.Normal)} hours.");
    }

    /// Customer reply re-opens resolved/closed threads and resets the SLA clock for a next waiting cycle.
    internal static async Task<SaveResult> ReplyToTicketAsync(Guid uid, Guid id, string? message, AppDb db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message))
            return new SaveResult(Errors: new Dictionary<string, string> { ["message"] = "Write your reply." });
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id && t.UserId == uid, ct);
        if (ticket is null)
            return new SaveResult(NotFound: true);

        var now = DateTime.UtcNow;
        db.TicketMessages.Add(new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorUserId = uid,
            IsStaff = false,
            Body = message.Trim(),
            At = now,
        });
        ticket.LastMessageAt = now;
        ticket.UpdatedAt = now;
        if (ticket.Status is TicketStatuses.Resolved or TicketStatuses.Closed)
        {
            ticket.Status = TicketStatuses.Open;
            ticket.ResolvedAt = null;
            ticket.DueAt = now.AddHours(TicketSla.Hours(ticket.Priority));
        }
        await db.SaveChangesAsync(ct);
        return new SaveResult(FlashType: "success", FlashMessage: "Reply sent.");
    }

    // ---------------------------------------------------------------- guest (public form on /support/policy)

    internal static async Task<SaveResult> SaveGuestComplaintAsync(
        string? name, string? email, string? subject, string? category, string? message, string? orderNumber,
        AppDb db, CancellationToken ct)
    {
        email = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2)
            return new SaveResult(Errors: new Dictionary<string, string> { ["name"] = "Enter your full name." });
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return new SaveResult(Errors: new Dictionary<string, string> { ["email"] = "Enter a valid email so we can respond." });
        subject = subject?.Trim();
        if (string.IsNullOrWhiteSpace(subject) || subject.Length < 4)
            return new SaveResult(Errors: new Dictionary<string, string> { ["subject"] = "Give the complaint a subject (4+ characters)." });
        if (string.IsNullOrWhiteSpace(message))
            return new SaveResult(Errors: new Dictionary<string, string> { ["message"] = "Tell us what went wrong." });
        var cat = CategoryList.Contains(category) ? category! : TicketCategories.Other;

        Guid? orderId = null;
        Guid? userId = null;
        if (!string.IsNullOrWhiteSpace(orderNumber))
        {
            var match = await db.Orders.AsNoTracking()
                .Where(o => o.Number == orderNumber.Trim())
                .Select(o => new { o.Id, o.UserId })
                .FirstOrDefaultAsync(ct);
            if (match is not null)
            {
                orderId = match.Id;
                userId = match.UserId;
            }
        }

        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ticket = new Ticket
        {
            Number = await OrderNumbers.NextTicketAsync(db, ct),
            UserId = userId,
            GuestEmail = userId is null ? email : null,
            OrderId = orderId,
            Subject = subject,
            Category = cat,
            Status = TicketStatuses.Received,
            Priority = TicketPriorities.Normal,
            DueAt = now.AddHours(TicketSla.Hours(TicketPriorities.Normal)),
            LastMessageAt = now,
        };
        db.Tickets.Add(ticket);
        db.TicketMessages.Add(new TicketMessage
        {
            Ticket = ticket,
            AuthorUserId = null,
            IsStaff = false,
            Body = $"[{name?.Trim()}: {email}]\n{message.Trim()}",
            At = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new SaveResult(FlashType: "success", FlashMessage: "Complaint received — we reply by email within 72 hours.");
    }

    // ---------------------------------------------------------------- admin inbox

    /// Filters per contract: mine (threads this admin touched) / overdue / waiting / done.
    internal static async Task<(AdminTicketsPage Tickets, string Filter)> GetAdminTicketsAsync(
        string? filter, int? page, Guid uid, AppDb db, CancellationToken ct)
    {
        var pageNo = Math.Max(page ?? 1, 1);
        var size = 25;
        var now = DateTime.UtcNow;
        var query = db.Tickets.AsNoTracking();
        query = filter switch
        {
            "mine" => query.Where(t => db.TicketMessages.Any(m => m.TicketId == t.Id && m.AuthorUserId == uid)),
            "overdue" => query.Where(t => t.ResolvedAt == null && t.DueAt < now),
            "waiting" => query.Where(t => t.Status == TicketStatuses.Received || t.Status == TicketStatuses.Open || t.Status == TicketStatuses.PendingCustomer),
            "done" => query.Where(t => t.Status == TicketStatuses.Resolved || t.Status == TicketStatuses.Closed),
            _ => query,
        };
        var rows = await query
            .OrderByDescending(t => t.ResolvedAt == null && t.DueAt < now ? 1 : 0)
            .ThenBy(t => t.ResolvedAt == null ? 0 : 1)
            .ThenBy(t => t.DueAt)
            .Skip((pageNo - 1) * size).Take(size + 1)
            .Select(t => new
            {
                t.Id,
                t.Number,
                t.Subject,
                t.Category,
                t.Status,
                t.Priority,
                t.DueAt,
                t.CreatedAt,
                t.GuestEmail,
                CustomerName = t.User != null ? t.User.Name : null,
                CustomerEmail = t.User != null ? t.User.Email : null,
                MessageCount = db.TicketMessages.Count(m => m.TicketId == t.Id),
            })
            .ToListAsync(ct);
        return (new AdminTicketsPage(rows.Take(size).Select(r => (object)r).ToList(), pageNo, rows.Count > size), filter ?? "all");
    }

    internal static async Task<object?> GetTicketDetailAsync(string number, AppDb db, CancellationToken ct)
    {
        return await db.Tickets.AsNoTracking()
            .Where(t => t.Number == number)
            .Select(t => new
            {
                t.Id,
                t.Number,
                t.Subject,
                t.Category,
                t.Status,
                t.Priority,
                t.DueAt,
                t.FirstResponseAt,
                t.ResolvedAt,
                t.CreatedAt,
                t.GuestEmail,
                CustomerName = t.User != null ? t.User.Name : null,
                CustomerEmail = t.User != null ? t.User.Email : null,
                OrderNumber = t.Order != null ? t.Order.Number : null,
                Messages = db.TicketMessages.Where(m => m.TicketId == t.Id)
                    .OrderBy(m => m.At).ThenBy(m => m.Id)
                    .Select(m => new { m.Id, m.IsStaff, m.Body, m.At })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);
    }

    /// Staff reply sets FirstResponseAt (SLA), notifies the customer, and mails the thread.
    internal static async Task<SaveResult> SaveAdminReplyAsync(
        Guid? authorId, Guid id, string? message,
        AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message))
            return new SaveResult(Errors: new Dictionary<string, string> { ["message"] = "Write the reply." });
        var ticket = await db.Tickets.Include(t => t.User).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null)
            return new SaveResult(NotFound: true);

        var now = DateTime.UtcNow;
        db.TicketMessages.Add(new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorUserId = authorId,
            IsStaff = true,
            Body = message.Trim(),
            At = now,
        });
        ticket.FirstResponseAt ??= now;
        ticket.LastMessageAt = now;
        ticket.UpdatedAt = now;
        if (ticket.Status == TicketStatuses.Received)
            ticket.Status = TicketStatuses.Open;
        await db.SaveChangesAsync(ct);

        if (ticket.User is { } user)
        {
            db.Alerts.Add(new Alert
            {
                UserId = user.Id,
                Kind = AlertKinds.Reply,
                Title = $"Support replied on {ticket.Number}",
                Body = "A support agent answered your complaint.",
                Href = "/account/support",
            });
            await db.SaveChangesAsync(ct);
            await realtime.NotifyUserAsync(user.Id, $"Support replied on {ticket.Number}", "A support agent answered your complaint.", "/account/support", ct);
            await jobs.EnqueueAsync(db, new TicketReplyEmail(ticket.Id), ct: ct);
        }
        else if (ticket.GuestEmail is { } guest)
        {
            await jobs.EnqueueAsync(db, new TicketReplyEmail(ticket.Id), ct: ct);
        }
        return new SaveResult();
    }

    internal static async Task<SaveResult> DecideTicketAsync(
        Guid? authorId, Guid id, string action, string? note,
        AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var ticket = await db.Tickets.Include(t => t.User).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null)
            return new SaveResult(NotFound: true);
        var now = DateTime.UtcNow;
        switch (action)
        {
            case "resolve":
                if (ticket.Status == TicketStatuses.Closed) return new SaveResult();
                ticket.Status = TicketStatuses.Resolved;
                ticket.ResolvedAt = now;
                break;
            case "close":
                ticket.Status = TicketStatuses.Closed;
                ticket.ResolvedAt ??= now;
                break;
            case "pending":
                ticket.Status = TicketStatuses.PendingCustomer;
                break;
            case "escalate":
                ticket.Priority = ticket.Priority switch
                {
                    TicketPriorities.Low => TicketPriorities.Normal,
                    TicketPriorities.Normal => TicketPriorities.High,
                    _ => TicketPriorities.Urgent,
                };
                ticket.DueAt = now.AddHours(TicketSla.Hours(ticket.Priority));
                break;
            default:
                return new SaveResult(FlashType: "error", FlashMessage: "Unknown action.");
        }
        if (!string.IsNullOrWhiteSpace(note))
            db.TicketMessages.Add(new TicketMessage
            {
                TicketId = ticket.Id,
                AuthorUserId = authorId,
                IsStaff = true,
                Body = note.Trim(),
                At = now,
            });
        ticket.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new SaveResult();
    }
}

public sealed record AdminTicketsPage(IReadOnlyList<object> Data, int Page, bool HasMore) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}
