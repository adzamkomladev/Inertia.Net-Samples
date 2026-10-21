using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// In-app notification inbox; push comes lazily over SignalR, these rows are the durable memory.
/// Returns plain data; the Alerts.cs route handlers compose the Inertia responses.
public static partial class AlertsService
{
    internal static async Task<(AlertsPage Alerts, int UnreadCount)> GetInboxAsync(Guid uid, int? page, AppDb db, CancellationToken ct)
    {
        var pageNo = Math.Max(page ?? 1, 1);
        var size = 25;
        var rows = await db.Alerts.AsNoTracking().Where(a => a.UserId == uid)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((pageNo - 1) * size).Take(size + 1)
            .Select(a => new { a.Id, a.Kind, a.Title, a.Body, a.Href, a.ReadAt, a.CreatedAt })
            .ToListAsync(ct);
        return (
            new AlertsPage(rows.Take(size).Select(r => (object)r).ToList(), pageNo, rows.Count > size),
            await db.Alerts.CountAsync(a => a.UserId == uid && a.ReadAt == null, ct));
    }

    internal static async Task MarkReadAsync(Guid uid, Guid id, AppDb db, CancellationToken ct) =>
        await db.Alerts.Where(a => a.Id == id && a.UserId == uid && a.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReadAt, DateTime.UtcNow), ct);

    internal static async Task MarkAllReadAsync(Guid uid, AppDb db, CancellationToken ct) =>
        await db.Alerts.Where(a => a.UserId == uid && a.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReadAt, DateTime.UtcNow), ct);
}

public sealed record AlertsPage(IReadOnlyList<object> Data, int Page, bool HasMore) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}
