using Inkwell.Features.Discovery;
using Inkwell.Infrastructure;

namespace Inkwell.Jobs.Handlers;

public sealed class RefreshInterestProfileHandler(DiscoveryService discovery) : IJobHandler<RefreshInterestProfile>
{
    public async Task HandleAsync(RefreshInterestProfile job, CancellationToken ct) => await discovery.RefreshProfileAsync(job.UserId, ct);
}

public sealed class SendRecommendationDigestHandler(DigestService digest) : IJobHandler<SendRecommendationDigest>
{
    public async Task HandleAsync(SendRecommendationDigest job, CancellationToken ct) => await digest.SendAsync(job.DeliveryId, ct);
}
