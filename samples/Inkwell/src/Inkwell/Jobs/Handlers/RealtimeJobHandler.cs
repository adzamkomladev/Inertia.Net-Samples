using Inkwell.Infrastructure;
using Runwick;

namespace Inkwell.Jobs.Handlers;

public sealed record PostChanged(Guid PostId, Guid? Actor, string? Notification, bool Published) : IQueueJob;
public sealed record PostRemoved(Guid PostId, Guid Owner, bool WasPublic) : IQueueJob;

public sealed class PostChangedHandler(RealtimeDelivery delivery) : global::Runwick.IJobHandler<PostChanged>
{
    public async Task HandleAsync(PostChanged job, CancellationToken ct) =>
        await delivery.ChangedAsync(job.PostId, job.Actor, job.Notification, job.Published);
}

public sealed class PostRemovedHandler(RealtimeDelivery delivery) : global::Runwick.IJobHandler<PostRemoved>
{
    public async Task HandleAsync(PostRemoved job, CancellationToken ct) =>
        await delivery.RemovedAsync(job.PostId, job.Owner, job.WasPublic);
}
