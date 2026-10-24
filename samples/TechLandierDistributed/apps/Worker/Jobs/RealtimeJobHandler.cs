using TechLandier.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

namespace TechLandier.Infrastructure;

public sealed class RealtimePushHandler(IHubContext<StoreHub> hub, AppDb db) : IJobHandler<RealtimePush>
{
    public async Task HandleAsync(RealtimePush job, CancellationToken ct)
    {
        var clients = job.UserId is { } user ? hub.Clients.User(user)
            : job.Group is { } group ? hub.Clients.Group(group) : hub.Clients.All;
        // Membership claims can outlive a role change on an already-open socket.
        if (job.UserId is null && job.Group == "admins")
        {
            var recipients = await db.Users.AsNoTracking().Where(u => u.Role == "admin").Select(u => u.Id.ToString()).ToListAsync(ct);
            clients = hub.Clients.Users(recipients);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await clients.SendAsync(job.Method, job.Payload, timeout.Token);
    }
}
