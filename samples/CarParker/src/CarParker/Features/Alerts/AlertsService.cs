using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;

namespace CarParker.Features.Alerts;

/// <summary>Durable user alerts (bell/menu) plus realtime pushes to the same user and the staff group.</summary>
public static class AlertsService
{
    /// <summary>Inserts an alert for one user and pushes a live "Notification" to their open tabs.</summary>
    public static async Task<Alert> CreateAsync(AppDb db, RealtimeEvents realtime, Guid userId, string kind, string title, string? body = null, string? href = null, bool unread = false, CancellationToken ct = default)
    {
        var alert = new Alert { UserId = userId, Kind = kind, Title = title, Body = body, Href = href, At = DateTime.UtcNow, ReadAt = unread ? null : DateTime.UtcNow };
        await db.Alerts.InsertOneAsync(alert, cancellationToken: ct);
        if (unread) await realtime.NotifyUserAsync(userId, title, href, ct);
        return alert;
    }

    /// <summary>Fan-out a realtime push to every admin/guard (no alert rows; dashboards show counts).</summary>
    public static Task StaffPushAsync(RealtimeEvents realtime, string message, string? href = null, CancellationToken ct = default) =>
        realtime.NotifyStaffAsync(message, href, ct);

    public static async Task<int> CountUnreadAsync(AppDb db, Guid userId, CancellationToken ct) =>
        (int)await db.Alerts.CountDocumentsAsync(x => x.UserId == userId && x.ReadAt == null, cancellationToken: ct);

    /// <summary>Most recent 100 alerts for the account bell page.</summary>
    public static async Task<List<Alert>> ListAsync(AppDb db, Guid userId, CancellationToken ct) =>
        await db.Alerts.Find(a => a.UserId == userId).SortByDescending(a => a.At).Limit(100).ToListAsync(ct);

    public static Task MarkReadAsync(AppDb db, Guid userId, Guid[] ids, CancellationToken ct) =>
        db.Alerts.UpdateManyAsync(x => x.UserId == userId && ids.Contains(x.Id) && x.ReadAt == null,
            Builders<Alert>.Update.Set(x => x.ReadAt, DateTime.UtcNow), cancellationToken: ct);
}
