using System.Text.Json;
using CarParker.Data;
using CarParker.Features.Alerts;
using Inertia.Net;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;

namespace CarParker.Infrastructure;

public sealed record RealtimePush(string Method, JsonElement Payload, string? Group = null, string? UserId = null) : IJob;

public sealed class RealtimePushHandler(IHubContext<ParkHub> hub, IInertiaLive live, AppDb db) : IJobHandler<RealtimePush>
{
    public async Task HandleAsync(RealtimePush job, CancellationToken ct)
    {
        var clients = job.UserId is { } userId ? hub.Clients.User(userId)
            : job.Group is { } group ? hub.Clients.Group(group) : hub.Clients.All;
        // Membership claims can outlive a role change on an already-open socket.
        List<Guid>? staff = null;
        if (job.UserId is null && job.Group == "staff")
        {
            staff = await db.Users.Find(u => u.Role == UserRole.Admin || u.Role == UserRole.Guard).Project(u => u.Id).ToListAsync(ct);
            clients = hub.Clients.Users(staff.Select(id => id.ToString()).ToList());
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await clients.SendAsync(job.Method, job.Payload, timeout.Token);
        // The live transport refreshes the props that listen on per-user and staff channels; publish points
        // remove their cache entries before enqueuing, so every reload reads fresh rows. Storefront pulses
        // (SpotChanged/LotPulse) stay on the raw hub above: those pages hold Scroll props, which cannot be live.
        if (job.UserId is { } user && job.Method == "Notification")
            await live.PublishAsync(AlertEvents.Created(Guid.Parse(user)), cancellationToken: timeout.Token);
        if (staff is not null)
        {
            if (job.Method == "Notification")
                foreach (var id in staff)
                    await live.PublishAsync(AlertEvents.Created(id), cancellationToken: timeout.Token);
            else
                await live.PublishAsync(StaffEvents.Board(), cancellationToken: timeout.Token);
        }
    }
}
