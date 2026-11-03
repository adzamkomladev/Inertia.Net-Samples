using System.Text.Json;
using Inertia.Net;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;

namespace TechLandier.Infrastructure;

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
        // The Inertia live transport rides the same backplane: these publishes refresh the props that
        // listen for the events on the per-user, per-order and admins channels. The raw events above
        // keep carrying the toast wording; values are never published, since pages reload their own data.
        if (job.UserId is { } notifyUser && job.Method == RealtimeNames.NotificationEvent)
        {
            await live.PublishAsync(new LiveEvent(RealtimeNames.NotificationEvent, LiveChannel.Private(RealtimeNames.UserChannelPrefix + Guid.Parse(notifyUser))), cancellationToken: timeout.Token);
            return;
        }
        if (admins is not null && job.Method == RealtimeNames.NotificationEvent)
        {
            foreach (var id in admins)
                await live.PublishAsync(new LiveEvent(RealtimeNames.NotificationEvent, LiveChannel.Private(RealtimeNames.UserChannelPrefix + id)), cancellationToken: timeout.Token);
            return;
        }
        if (admins is not null)
            await live.PublishAsync(new LiveEvent(RealtimeNames.AdminPulseEvent, LiveChannel.Private(RealtimeNames.AdminChannel)), cancellationToken: timeout.Token);
        if (job.UserId is { } owner && job.Method == RealtimeNames.TimelineEvent)
        {
            var number = job.Payload.GetProperty("orderNumber").GetString();
            await live.PublishAsync(new LiveEvent(RealtimeNames.TimelineEvent, LiveChannel.Public(RealtimeNames.OrderChannelPrefix + number)), cancellationToken: timeout.Token);
        }
    }
}
