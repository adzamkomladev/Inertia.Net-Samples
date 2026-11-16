using Inkwell.Infrastructure;

namespace Inkwell.Jobs.Handlers;

public sealed class SendRecommendationDigestHandler(DigestService digest) : global::Runwick.IJobHandler<SendRecommendationDigest>
{
    public async Task HandleAsync(SendRecommendationDigest job, CancellationToken ct) => await digest.SendAsync(job.DeliveryId, ct);
}
