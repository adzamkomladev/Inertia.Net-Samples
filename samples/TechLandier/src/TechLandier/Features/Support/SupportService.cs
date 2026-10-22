using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// Complaints: the public guest form, customer tickets and the admin inbox. Numbers are T-0001; the SLA depends on priority.
public sealed class SupportService(AppDb db, JobQueue jobs, RealtimeEvents realtime)
{
    const int AdminPageSize = 25;

    public static readonly string[] Categories =
    [
        TicketCategories.ProductFault, TicketCategories.WrongItem, TicketCategories.LateDelivery, TicketCategories.DeliveryIssue,
        TicketCategories.Refund, TicketCategories.Warranty, TicketCategories.Insurance, TicketCategories.Other,
    ];

    static string Category(string? value) => Categories.Contains(value) ? value! : TicketCategories.Other;

    static string NextPriority(string priority) => priority switch
    {
        TicketPriorities.Low => TicketPriorities.Normal,
        TicketPriorities.Normal => TicketPriorities.High,
        _ => TicketPriorities.Urgent,
    };

    // ---------------------------------------------------------------- account

    public async Task<List<AccountTicketRow>> TicketsAsync(Guid uid, CancellationToken ct) =>
        await db.Tickets.AsNoTracking()
            .Where(t => t.UserId == uid)
            .OrderByDescending(t => t.UpdatedAt).ThenByDescending(t => t.Id)
            .Take(100)
            .Select(t => new AccountTicketRow(
                t.Id, t.Number, t.Subject, t.Category, t.Status, t.Priority, t.DueAt, t.ResolvedAt, t.CreatedAt,
                t.Order != null ? t.Order.Number : null,
                db.TicketMessages.Where(m => m.TicketId == t.Id).OrderBy(m => m.At).ThenBy(m => m.Id)
                    .Select(m => new MessageRow(m.Id, m.IsStaff, m.Body, m.At)).ToList()))
            .ToListAsync(ct);

    public async Task<SaveResult> CreateTicketAsync(Guid uid, TicketInput input, CancellationToken ct)
    {
        var subject = input.Subject?.Trim();
        if (subject is not { Length: >= 4 }) return SaveResult.Invalid("subject", "Give the thread a subject (4+ characters).");
        if (string.IsNullOrWhiteSpace(input.Message)) return SaveResult.Invalid("message", "Describe the problem.");

        Guid? orderId = null;
        if (input.OrderNumber?.Trim() is { Length: > 0 } orderNumber)
            orderId = await db.Orders.Where(o => o.Number == orderNumber && o.UserId == uid).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct);

        var number = await OpenAsync(uid, null, orderId, subject, input.Category, input.Message, uid, ct);
        return SaveResult.Success($"Ticket {number} opened — support replies within {TicketSla.Hours(TicketPriorities.Normal)} hours.");
    }

    /// A customer reply re-opens resolved or closed threads and restarts the SLA clock.
    public async Task<SaveResult> ReplyAsync(Guid uid, Guid id, ReplyInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Message)) return SaveResult.Invalid("message", "Write your reply.");
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id && t.UserId == uid, ct);
        if (ticket is null) return SaveResult.Missing;

        var now = DateTime.UtcNow;
        db.TicketMessages.Add(new TicketMessage { TicketId = ticket.Id, AuthorUserId = uid, Body = input.Message.Trim(), At = now });
        ticket.LastMessageAt = ticket.UpdatedAt = now;
        if (ticket.Status is TicketStatuses.Resolved or TicketStatuses.Closed)
        {
            ticket.Status = TicketStatuses.Open;
            ticket.ResolvedAt = null;
            ticket.DueAt = now.AddHours(TicketSla.Hours(ticket.Priority));
        }
        await db.SaveChangesAsync(ct);
        return SaveResult.Success("Reply sent.");
    }

    // ---------------------------------------------------------------- guest (public form on /support/policy)

    public async Task<SaveResult> GuestComplaintAsync(GuestComplaintInput input, CancellationToken ct)
    {
        var email = input.Email?.Trim().ToLowerInvariant();
        var name = input.Name?.Trim();
        var subject = input.Subject?.Trim();
        if (name is not { Length: >= 2 }) return SaveResult.Invalid("name", "Enter your full name.");
        if (email is null || !email.Contains('@')) return SaveResult.Invalid("email", "Enter a valid email so we can respond.");
        if (subject is not { Length: >= 4 }) return SaveResult.Invalid("subject", "Give the complaint a subject (4+ characters).");
        if (string.IsNullOrWhiteSpace(input.Message)) return SaveResult.Invalid("message", "Tell us what went wrong.");

        // A known order number links the complaint to its customer.
        var match = input.OrderNumber?.Trim() is { Length: > 0 } orderNumber
            ? await db.Orders.Where(o => o.Number == orderNumber).Select(o => new { o.Id, o.UserId }).FirstOrDefaultAsync(ct)
            : null;

        await OpenAsync(match?.UserId, match is null ? email : null, match?.Id, subject, input.Category,
            $"[{name}: {email}]\n{input.Message.Trim()}", null, ct);
        return SaveResult.Success("Complaint received — we reply by email within 72 hours.");
    }

    /// Creates the ticket and its first message under the numbering lock.
    async Task<string> OpenAsync(Guid? userId, string? guestEmail, Guid? orderId, string subject, string? category, string message, Guid? authorId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext('techlandier-ticket-number'))", ct);
        var last = await db.Tickets.Where(t => t.Number.StartsWith("T-")).OrderByDescending(t => t.Number).Select(t => t.Number).FirstOrDefaultAsync(ct);
        var ticket = new Ticket
        {
            Number = $"T-{(last is null ? 1 : int.Parse(last[2..]) + 1):D4}",
            UserId = userId,
            GuestEmail = guestEmail,
            OrderId = orderId,
            Subject = subject,
            Category = Category(category),
            DueAt = now.AddHours(TicketSla.Hours(TicketPriorities.Normal)),
            LastMessageAt = now,
        };
        db.Tickets.Add(ticket);
        db.TicketMessages.Add(new TicketMessage { TicketId = ticket.Id, AuthorUserId = authorId, Body = message.Trim(), At = now });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ticket.Number;
    }

    // ---------------------------------------------------------------- admin inbox

    /// Filters: mine (threads this admin wrote in), overdue, waiting, done.
    public async Task<Paged<AdminTicketRow>> AdminTicketsAsync(string? filter, int? page, Guid uid, CancellationToken ct)
    {
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
        return await query
            .OrderByDescending(t => t.ResolvedAt == null && t.DueAt < now ? 1 : 0)
            .ThenBy(t => t.ResolvedAt == null ? 0 : 1)
            .ThenBy(t => t.DueAt)
            .Select(t => new AdminTicketRow(
                t.Id, t.Number, t.Subject, t.Category, t.Status, t.Priority, t.DueAt, t.CreatedAt, t.GuestEmail,
                t.User != null ? t.User.Name : null, t.User != null ? t.User.Email : null,
                db.TicketMessages.Count(m => m.TicketId == t.Id)))
            .ToPagedAsync(page, AdminPageSize, ct);
    }

    public async Task<AdminTicketDetail?> TicketDetailAsync(string number, CancellationToken ct) =>
        await db.Tickets.AsNoTracking()
            .Where(t => t.Number == number)
            .Select(t => new AdminTicketDetail(
                t.Id, t.Number, t.Subject, t.Category, t.Status, t.Priority, t.DueAt, t.FirstResponseAt, t.ResolvedAt, t.CreatedAt, t.GuestEmail,
                t.User != null ? t.User.Name : null, t.User != null ? t.User.Email : null, t.Order != null ? t.Order.Number : null,
                db.TicketMessages.Where(m => m.TicketId == t.Id).OrderBy(m => m.At).ThenBy(m => m.Id)
                    .Select(m => new MessageRow(m.Id, m.IsStaff, m.Body, m.At)).ToList()))
            .FirstOrDefaultAsync(ct);

    /// A staff reply sets FirstResponseAt (SLA), notifies the customer and mails the thread.
    public async Task<SaveResult> AdminReplyAsync(Guid authorId, Guid id, ReplyInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Message)) return SaveResult.Invalid("message", "Write the reply.");
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return SaveResult.Missing;

        var now = DateTime.UtcNow;
        db.TicketMessages.Add(new TicketMessage { TicketId = ticket.Id, AuthorUserId = authorId, IsStaff = true, Body = input.Message.Trim(), At = now });
        ticket.FirstResponseAt ??= now;
        ticket.LastMessageAt = ticket.UpdatedAt = now;
        if (ticket.Status == TicketStatuses.Received) ticket.Status = TicketStatuses.Open;
        if (ticket.UserId is { } userId)
            db.Alerts.Add(new Alert
            {
                UserId = userId, Kind = AlertKinds.Reply, Title = $"Support replied on {ticket.Number}",
                Body = "A support agent answered your complaint.", Href = "/account/support",
            });
        if (ticket.UserId is not null || ticket.GuestEmail is not null) jobs.Enqueue(db, new TicketReplyEmail(ticket.Id));
        await db.SaveChangesAsync(ct);
        jobs.Wake();
        if (ticket.UserId is { } uid)
            await realtime.NotifyUserAsync(uid, $"Support replied on {ticket.Number}", "A support agent answered your complaint.", "/account/support", ct);
        return SaveResult.Done;
    }

    public async Task<SaveResult> DecideAsync(Guid authorId, Guid id, TicketActionInput input, CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return SaveResult.Missing;
        var now = DateTime.UtcNow;
        switch (input.Action)
        {
            case "resolve":
                if (ticket.Status == TicketStatuses.Closed) return SaveResult.Done;
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
                ticket.Priority = NextPriority(ticket.Priority);
                ticket.DueAt = now.AddHours(TicketSla.Hours(ticket.Priority));
                break;
            default:
                return SaveResult.Error("Unknown action.");
        }
        if (!string.IsNullOrWhiteSpace(input.Note))
            db.TicketMessages.Add(new TicketMessage { TicketId = ticket.Id, AuthorUserId = authorId, IsStaff = true, Body = input.Note.Trim(), At = now });
        ticket.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return SaveResult.Done;
    }
}
