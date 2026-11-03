using Inertia.Net;

namespace Inkwell.Features.Notifications;

/// <summary>
/// Live events on the per-user channel. The same definition names the prop side and the publish side, so they cannot
/// drift apart: the shared summary badge and the inbox list refresh when a notification is created or read elsewhere.
/// </summary>
public static class NotificationEvents
{
    public static LiveEvent Created(Guid userId) => new("Notification", LiveChannel.Private($"users.{userId}"));
}

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
