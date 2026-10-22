using Inkwell.Features.Analytics;

namespace Inkwell.Jobs.Handlers;

public sealed class PostViewedHandler(ViewTracker views) : IJobHandler<PostViewed>
{
    public Task HandleAsync(PostViewed job, CancellationToken ct) => views.RecordAsync(job, ct);
}
