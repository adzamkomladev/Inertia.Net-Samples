using Inkwell.Infrastructure;
using Runwick;

namespace Inkwell.Jobs.Handlers;

public sealed record PostChanged(Guid PostId, Guid? Actor, string? Notification, bool Published) : IQueueJob;
public sealed record PostRemoved(Guid PostId, Guid Owner, bool WasPublic) : IQueueJob;
/// <summary>
/// One trailing "the dashboards can reload" publish per owner per gate window: the first event of a burst is sent at
/// once by <see cref="RealtimeDelivery"/>, the rest of the burst folds into this job (unique while it is queued), and
/// it publishes once when it runs. A burst of writes costs two reload waves instead of one per write.
/// </summary>
public sealed record AnalyticsReload(Guid OwnerId) : IQueueJob;

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

public sealed class AnalyticsReloadHandler(RealtimeDelivery delivery) : global::Runwick.IJobHandler<AnalyticsReload>
{
    public async Task HandleAsync(AnalyticsReload job, CancellationToken ct) =>
        await delivery.PublishAnalyticsTrailingAsync(job.OwnerId, ct);
}
