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
    /// <summary>"like", "comment" or "notice"; the client picks wording and icon from it.</summary>
    public string Kind { get; set; } = NotificationKinds.Notice;
    /// <summary>Who caused it. Names and avatars are read at display time, so they stay current.</summary>
    public Guid? ActorId { get; set; }
    public Guid? PostId { get; set; }
    /// <summary>Plain-text fallback for clients and e-mail that do not render the structured fields.</summary>
    public required string Title { get; set; }
    public required string Href { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}
public static class NotificationKinds
{
    public const string Like = "like";
    public const string Comment = "comment";
    public const string Notice = "notice";
}

public sealed record InboxActor(Guid Id, string Name, string? AvatarUrl);

/// <summary>One inbox row as the client sees it: who, what and on which essay.</summary>
public sealed record InboxNotice(Guid Id, string Kind, string Title, string Href, DateTime At, DateTime? ReadAt, InboxActor? Actor, string? PostTitle);

public sealed record NotificationSummary(int UnreadCount, List<InboxNotice> Items);
