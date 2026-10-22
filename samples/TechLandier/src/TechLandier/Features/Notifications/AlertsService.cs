using Microsoft.EntityFrameworkCore;
using TechLandier.Data;

namespace TechLandier.Features;

/// The in-app notification inbox. Pushes arrive over SignalR; these rows are the durable copy.
public sealed class AlertsService(AppDb db)
{
    public async Task<NotificationSummary> SummaryAsync(Guid uid, CancellationToken ct) =>
        new(await db.Alerts.CountAsync(a => a.UserId == uid && a.ReadAt == null, ct),
            await db.Alerts.AsNoTracking().Where(a => a.UserId == uid).OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
                .Take(10).Select(a => new AlertRow(a.Id, a.Kind, a.Title, a.Body, a.Href, a.ReadAt, a.CreatedAt)).ToListAsync(ct));

    public async Task<(Paged<AlertRow> Alerts, int Unread)> InboxAsync(Guid uid, int? page, CancellationToken ct)
    {
        var alerts = await db.Alerts.AsNoTracking()
            .Where(a => a.UserId == uid)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Select(a => new AlertRow(a.Id, a.Kind, a.Title, a.Body, a.Href, a.ReadAt, a.CreatedAt))
            .ToPagedAsync(page, 25, ct);
        return (alerts, await db.Alerts.CountAsync(a => a.UserId == uid && a.ReadAt == null, ct));
    }

    public async Task MarkReadAsync(Guid uid, Guid id, CancellationToken ct) =>
        await db.Alerts.Where(a => a.Id == id && a.UserId == uid && a.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReadAt, DateTime.UtcNow), ct);

    public async Task MarkAllReadAsync(Guid uid, CancellationToken ct) =>
        await db.Alerts.Where(a => a.UserId == uid && a.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReadAt, DateTime.UtcNow), ct);
}
