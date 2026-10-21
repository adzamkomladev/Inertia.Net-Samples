using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Infrastructure;

namespace VuePgBlog.Features;

/// In-app notification inbox; push comes lazily over SignalR, these rows are the durable memory.
public static partial class AlertsService
{
    internal static async Task<IResult> InboxAsync(int? page, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var uid = ctx.User.UserId();
        var pageNo = Math.Max(page ?? 1, 1);
        var size = 25;
        var rows = await db.Alerts.AsNoTracking().Where(a => a.UserId == uid)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((pageNo - 1) * size).Take(size + 1)
            .Select(a => new { a.Id, a.Kind, a.Title, a.Body, a.Href, a.ReadAt, a.CreatedAt })
            .ToListAsync(ct);
        return Render(
            "account/Alerts",
            new InertiaProps
            {
                ["alerts"] = new AlertsPage(rows.Take(size).Select(r => (object)r).ToList(), pageNo, rows.Count > size),
                ["unreadCount"] = await db.Alerts.CountAsync(a => a.UserId == uid && a.ReadAt == null, ct),
            }
        );
    }

    internal static async Task<IResult> ReadAsync(Guid id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        await db.Alerts.Where(a => a.Id == id && a.UserId == ctx.User.UserId() && a.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReadAt, DateTime.UtcNow), ct);
        return Back();
    }

    internal static async Task<IResult> ReadAllAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        await db.Alerts.Where(a => a.UserId == ctx.User.UserId() && a.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReadAt, DateTime.UtcNow), ct);
        return Back().WithFlash("toast", new Toast("success", "All notifications marked as read."));
    }
}

public sealed record AlertsPage(IReadOnlyList<object> Data, int Page, bool HasMore) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}
