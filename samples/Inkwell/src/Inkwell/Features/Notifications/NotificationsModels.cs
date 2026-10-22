namespace Inkwell.Features.Notifications;

public sealed class Notification
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public required string Title { get; set; }
    public required string Href { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}
public sealed record NotificationSummary(int UnreadCount, List<Notification> Items);
