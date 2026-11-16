using Inkwell.Data;
using Inertia.Net;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Features.Notifications;

public sealed class NotificationsService(AppDb db, IInertiaLive live, IInertiaCache cache)
{
    public async Task<NotificationSummary> SummaryAsync(Guid uid, CancellationToken ct) =>
        new(await db.Notifications.CountAsync(n => n.UserId == uid && n.ReadAt == null, ct), await ListAsync(uid, 10, ct));

    public async Task<List<InboxNotice>> ListAsync(Guid uid, CancellationToken ct) => await ListAsync(uid, 100, ct);

    /// <summary>Newest first, with the actor and the essay title joined in one query.</summary>
    async Task<List<InboxNotice>> ListAsync(Guid uid, int take, CancellationToken ct) =>
        await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == uid)
            .OrderByDescending(n => n.At).ThenByDescending(n => n.Id)
            .Take(take)
            .Select(n => new InboxNotice(n.Id, n.Kind, n.Title, n.Href, n.At, n.ReadAt,
                db.Users.Where(u => u.Id == n.ActorId).Select(u => new InboxActor(u.Id, u.DisplayName, u.AvatarUrl)).FirstOrDefault(),
                db.Posts.IgnoreQueryFilters().Where(p => p.Id == n.PostId).Select(p => p.Title).FirstOrDefault()))
            .ToListAsync(ct);

    public async Task MarkAllReadAsync(Guid uid, CancellationToken ct)
    {
        await db.Notifications.Where(n => n.UserId == uid && n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, DateTime.UtcNow), ct);
        // Invalidate rather than write through: a concurrent insert can otherwise be overwritten by an older summary.
        await NotificationsCache.InvalidateAsync(cache, uid, ct);
        // The reader's other sessions clear their badge on their own channel without reloading the page.
        await live.PublishAsync(NotificationEvents.Created(uid), cancellationToken: ct);
    }
}
