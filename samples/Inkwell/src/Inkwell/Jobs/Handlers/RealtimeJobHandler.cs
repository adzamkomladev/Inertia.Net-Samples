using Inkwell.Data;
using Inkwell.Infrastructure;

namespace Inkwell.Jobs.Handlers;

public sealed record PostChanged(Guid PostId, Guid? Actor, string? Notification, bool Published);
public sealed record PostRemoved(Guid PostId, Guid Owner, bool WasPublic);

public sealed class PostChangedHandler(RealtimeDelivery delivery) : IJobHandler<PostChanged>
{
    public Task HandleAsync(PostChanged job, CancellationToken ct) =>
        delivery.ChangedAsync(job.PostId, job.Actor, job.Notification, job.Published);
}

public sealed class PostRemovedHandler(RealtimeDelivery delivery) : IJobHandler<PostRemoved>
{
    public Task HandleAsync(PostRemoved job, CancellationToken ct) =>
        delivery.RemovedAsync(job.PostId, job.Owner, job.WasPublic);
}
