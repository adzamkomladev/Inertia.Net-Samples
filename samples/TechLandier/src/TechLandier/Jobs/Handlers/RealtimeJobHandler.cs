using System.Text.Json;
using TechLandier.Data;
using TechLandier.Features;
using Inertia.Net;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace TechLandier.Infrastructure;

public sealed record RealtimePush(string Method, JsonElement Payload, string? Group = null, string? UserId = null) : IJob;

public sealed class RealtimePushHandler(IHubContext<StoreHub> hub, IInertiaLive live, AppDb db) : IJobHandler<RealtimePush>
{
    public async Task HandleAsync(RealtimePush job, CancellationToken ct)
    {
        var clients = job.UserId is { } user ? hub.Clients.User(user)
            : job.Group is { } group ? hub.Clients.Group(group) : hub.Clients.All;
        // Membership claims can outlive a role change on an already-open socket.
        List<Guid>? admins = null;
        if (job.UserId is null && job.Group == "admins")
        {
            admins = await db.Users.AsNoTracking().Where(u => u.Role == "admin").Select(u => u.Id).ToListAsync(ct);
            clients = hub.Clients.Users(admins.Select(u => u.ToString()).ToList());
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await clients.SendAsync(job.Method, job.Payload, timeout.Token);
        // The live transport (Inertia.Net.Live.SignalR) refreshes props that listen on the per-user, per-order
        // and admins channels; publish points remove their cache entries before enqueuing, so every reload
        // reads fresh rows. The raw events above keep carrying the toast wording the client speaks.
        if (job.UserId is { } notifyUser && job.Method == "Notification")
        {
            await live.PublishAsync(UserEvents.Notification(Guid.Parse(notifyUser)), cancellationToken: timeout.Token);
            return;
        }
        if (admins is not null && job.Method == "Notification")
        {
            foreach (var id in admins)
                await live.PublishAsync(UserEvents.Notification(id), cancellationToken: timeout.Token);
            return;
        }
        if (admins is not null)
            await live.PublishAsync(AdminEvents.Pulse(), cancellationToken: timeout.Token);
        if (job.UserId is { } owner && job.Method == "OrderTimelineUpdated")
            await live.PublishAsync(OrderEvents.Timeline(job.Payload.GetProperty("orderNumber").GetString()!), cancellationToken: timeout.Token);
    }
}
