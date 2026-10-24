using Inertia.Net;

namespace CarParker.Features.Alerts;

/// <summary>
/// The shared alert summary is UI data: it is cached per user for five minutes and invalidated by every
/// create/read path, with a one-minute stale window as the fallback bound while the badge refreshes.
/// </summary>
public static class AlertsCache
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan StaleFor = TimeSpan.FromMinutes(1);

    public static string Key(Guid userId) => $"notifications:{userId}";
    public static string Tag(Guid userId) => $"notifications:{userId}";

    public static async Task InvalidateAsync(IInertiaCache cache, Guid userId, CancellationToken ct) =>
        await cache.RemoveByTagAsync(Tag(userId), ct);
}
