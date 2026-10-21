using VuePgBlog.Data;
namespace VuePgBlog.Features;

public static class TicketStatuses
{
    public const string Received = "received";
    public const string Open = "open";
    public const string PendingCustomer = "pending_customer";
    public const string Resolved = "resolved";
    public const string Closed = "closed";
}

public static class TicketPriorities
{
    public const string Low = "low";
    public const string Normal = "normal";
    public const string High = "high";
    public const string Urgent = "urgent";
}

public static class TicketCategories
{
    public const string ProductFault = "product_fault";
    public const string WrongItem = "wrong_item";
    public const string LateDelivery = "late_delivery";
    public const string DeliveryIssue = "delivery_issue";
    public const string Refund = "refund";
    public const string Warranty = "warranty";
    public const string Insurance = "insurance";
    public const string Other = "other";
}

/// SLA windows: urgent 24h, high 48h, normal 72h, low 7d.
public static class TicketSla
{
    public static int Hours(string priority) => priority switch
    {
        TicketPriorities.Urgent => 24,
        TicketPriorities.High => 48,
        TicketPriorities.Low => 168,
        _ => 72,
    };
}

public sealed class Ticket
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// Human ticket number, T-0001 style.
    public required string Number { get; set; }
    /// Null for guest complaints sent via the public support form.
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public string? GuestEmail { get; set; }
    public Guid? OrderId { get; set; }
    public Order? Order { get; set; }
    public required string Subject { get; set; }
    public string Category { get; set; } = TicketCategories.Other;
    public string Status { get; set; } = TicketStatuses.Received;
    public string Priority { get; set; } = TicketPriorities.Normal;
    /// First-response SLA deadline derived from priority at creation.
    public required DateTime DueAt { get; set; }
    public DateTime? FirstResponseAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? DemoKey { get; set; }
}

public sealed class TicketMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    /// Null when the message was written by staff (IsStaff = true) or a guest (GuestEmail on ticket).
    public Guid? AuthorUserId { get; set; }
    public bool IsStaff { get; set; }
    public required string Body { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}
