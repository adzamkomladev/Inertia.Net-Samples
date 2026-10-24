using System.Text.Json;
using CarParker.Data;
using MongoDB.Driver;
using Microsoft.AspNetCore.SignalR;

namespace CarParker.Infrastructure;

public sealed record RealtimePush(string Method, JsonElement Payload, string? Group = null, string? UserId = null) : IJob;

public sealed class RealtimePushHandler(IHubContext<ParkHub> hub, AppDb db) : IJobHandler<RealtimePush>
{
    public async Task HandleAsync(RealtimePush job, CancellationToken ct)
    {
        var clients = job.UserId is { } user ? hub.Clients.User(user)
            : job.Group is { } group ? hub.Clients.Group(group) : hub.Clients.All;
        // Membership claims can outlive a role change on an already-open socket.
        if (job.UserId is null && job.Group == "staff")
        {
            var recipients = await db.Users.Find(u => u.Role == UserRole.Admin || u.Role == UserRole.Guard).Project(u => u.Id).ToListAsync(ct);
            clients = hub.Clients.Users(recipients.Select(id => id.ToString()).ToList());
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await clients.SendAsync(job.Method, job.Payload, timeout.Token);
    }
}
