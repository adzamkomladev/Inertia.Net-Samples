using System.Text.Json;
using Microsoft.AspNetCore.SignalR;

namespace CarParker.Infrastructure;

public sealed record RealtimePush(string Method, JsonElement Payload, string? Group = null, string? UserId = null) : IJob;

public sealed class RealtimePushHandler(IHubContext<ParkHub> hub) : IJobHandler<RealtimePush>
{
    public async Task HandleAsync(RealtimePush job, CancellationToken ct)
    {
        var clients = job.UserId is { } user ? hub.Clients.User(user)
            : job.Group is { } group ? hub.Clients.Group(group) : hub.Clients.All;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await clients.SendAsync(job.Method, job.Payload, timeout.Token);
    }
}
