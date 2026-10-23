using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;
using Inertia.Net;

namespace CarParker.Features.Alerts;

/// <summary>Durable user alerts (bell page) plus realtime pushes to the user's open tabs.</summary>
public sealed class AlertsService(AppDb db, RealtimeEvents realtime, IInertiaCache cache)
{
    /// <summary>Stores an alert; only unread ones push a live notification (read ones are a plain audit trail).</summary>
    public async Task CreateAsync(Guid userId, string kind, string title, string? body = null, string? href = null, bool unread = true, CancellationToken ct = default, string? sourceKey = null)
    {
        var alert = NewAlert(userId, kind, title, body, href, unread);
        alert.SourceKey = sourceKey;
        try { await db.Alerts.InsertOneAsync(alert, cancellationToken: ct); }
        catch (MongoWriteException ex) when (sourceKey is not null && ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { return; }
        await AlertsCache.InvalidateAsync(cache, userId, ct);
        if (unread) await realtime.NotifyUserAsync(userId, title, href, ct);
    }

    public async Task CreateManyAsync(IReadOnlyCollection<Alert> alerts, CancellationToken ct = default)
    {
        if (alerts.Count == 0) return;
        await db.Alerts.InsertManyAsync(alerts, cancellationToken: ct);
        foreach (var userId in alerts.Select(a => a.UserId).Distinct())
            await AlertsCache.InvalidateAsync(cache, userId, ct);
        foreach (var alert in alerts.Where(a => a.ReadAt is null))
            await realtime.NotifyUserAsync(alert.UserId, alert.Title, alert.Href, ct);
    }

    public static Alert NewAlert(Guid userId, string kind, string title, string? body = null, string? href = null, bool unread = true) =>
        new() { UserId = userId, Kind = kind, Title = title, Body = body, Href = href, ReadAt = unread ? null : DateTime.UtcNow };

    public async Task<NotificationSummary> SummaryAsync(Guid userId, CancellationToken ct) =>
        new(await CountUnreadAsync(userId, ct), (await db.Alerts.Find(a => a.UserId == userId).SortByDescending(a => a.At).Limit(10).ToListAsync(ct)).Select(AlertsMappers.Row).ToList());

    public async Task MarkAllReadAsync(Guid userId, CancellationToken ct)
    {
        await db.Alerts.UpdateManyAsync(a => a.UserId == userId && a.ReadAt == null,
            Builders<Alert>.Update.Set(a => a.ReadAt, DateTime.UtcNow), cancellationToken: ct);
        // Write through the zero-unread summary so the next page render is an instant cache hit.
        var summary = await SummaryAsync(userId, ct);
        await cache.SetPropAsync(AlertsCache.Key(userId), summary, AlertsCache.FreshFor, AlertsCache.StaleFor,
            [AlertsCache.Tag(userId)], ct);
    }

    public async Task NotifyStaffAsync(string title, string? href, CancellationToken ct)
    {
        var adminsOnly = href != null && href.StartsWith("/admin", StringComparison.Ordinal);
        var users = await db.Users.Find(u => u.Role == UserRole.Admin || (!adminsOnly && u.Role == UserRole.Guard)).ToListAsync(ct);
        await CreateManyAsync(users.Select(u => NewAlert(u.Id, "operations", title, href: u.Role == UserRole.Admin && href == "/guard" ? "/admin/sessions" : href)).ToArray(), ct);
    }

    public async Task<int> CountUnreadAsync(Guid userId, CancellationToken ct) =>
        (int)await db.Alerts.CountDocumentsAsync(x => x.UserId == userId && x.ReadAt == null, cancellationToken: ct);

    public async Task<List<Alert>> ListAsync(Guid userId, CancellationToken ct) =>
        await db.Alerts.Find(a => a.UserId == userId).SortByDescending(a => a.At).Limit(100).ToListAsync(ct);

    public async Task MarkReadAsync(Guid userId, Guid[] ids, CancellationToken ct)
    {
        await db.Alerts.UpdateManyAsync(x => x.UserId == userId && ids.Contains(x.Id) && x.ReadAt == null,
            Builders<Alert>.Update.Set(x => x.ReadAt, DateTime.UtcNow), cancellationToken: ct);
        await AlertsCache.InvalidateAsync(cache, userId, ct);
    }
}
