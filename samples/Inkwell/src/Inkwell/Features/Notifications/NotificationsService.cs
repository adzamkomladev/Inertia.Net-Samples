using Inkwell.Data;
using Inertia.Net;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Features.Notifications;

public sealed class NotificationsService(AppDb db, IInertiaCache cache)
{
    public async Task<NotificationSummary> SummaryAsync(Guid uid, CancellationToken ct) =>
        new(await db.Notifications.CountAsync(n => n.UserId == uid && n.ReadAt == null, ct),
            await db.Notifications.AsNoTracking().Where(n => n.UserId == uid).OrderByDescending(n => n.At).ThenByDescending(n => n.Id).Take(10).ToListAsync(ct));

    public async Task<List<Notification>> ListAsync(Guid uid, CancellationToken ct) =>
        await db.Notifications.AsNoTracking().Where(n => n.UserId == uid).OrderByDescending(n => n.At).ThenByDescending(n => n.Id).Take(100).ToListAsync(ct);

    public async Task MarkAllReadAsync(Guid uid, CancellationToken ct)
    {
        await db.Notifications.Where(n => n.UserId == uid && n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, DateTime.UtcNow), ct);
        // Invalidate rather than write through: a concurrent insert can otherwise be overwritten by an older summary.
        await NotificationsCache.InvalidateAsync(cache, uid, ct);
    }
}
