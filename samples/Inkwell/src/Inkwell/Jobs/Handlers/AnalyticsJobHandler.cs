using Inkwell.Features.Analytics;

namespace Inkwell.Jobs.Handlers;

public sealed class PostViewedHandler(ViewTracker views) : IJobHandler<PostViewed>
{
    public async Task HandleAsync(PostViewed job, CancellationToken ct) => await views.RecordAsync(job, ct);
}
