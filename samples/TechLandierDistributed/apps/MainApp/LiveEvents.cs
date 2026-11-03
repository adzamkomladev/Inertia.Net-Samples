using Inertia.Net;
using TechLandier.Infrastructure;

namespace TechLandierDistributed.MainApp;

/// <summary>
/// The live-prop events MainApp declares. Built from the Core's RealtimeNames constants so the worker
/// (which reconstructs them from the queued push) always listens and publishes on the same channels.
/// </summary>
public static class LiveEvents
{
    /// <summary>The notification bell and the alerts page: each user's own private channel.</summary>
    public static LiveEvent Notification(Guid userId) =>
        new(RealtimeNames.NotificationEvent, LiveChannel.Private(RealtimeNames.UserChannelPrefix + userId));

    /// <summary>Admin dashboards, order boards and the delivery board: one private admins channel.</summary>
    public static LiveEvent AdminPulse() =>
        new(RealtimeNames.AdminPulseEvent, LiveChannel.Private(RealtimeNames.AdminChannel));

    /// <summary>Open tracking/account-order pages: the order's public channel, keyed by its number.</summary>
    public static LiveEvent Timeline(string orderNumber) =>
        new(RealtimeNames.TimelineEvent, LiveChannel.Public(RealtimeNames.OrderChannelPrefix + orderNumber));
}
