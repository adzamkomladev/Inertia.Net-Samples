using Inkwell.Data;
using Inkwell.Features.Discovery;
using Inkwell.Infrastructure;
using Inkwell.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inkwell.Jobs.Handlers;

public sealed class RefreshInterestProfileHandler(AppDb db, DiscoveryService discovery) : IJobHandler<RefreshInterestProfile>
{
    public Task HandleAsync(RefreshInterestProfile job, CancellationToken ct) => discovery.RefreshProfileAsync(db, job.UserId, ct);
}

public sealed class SendRecommendationDigestHandler(AppDb db, DigestService digest, EmailSender email, IOptions<AppOptions> app)
    : IJobHandler<SendRecommendationDigest>
{
    public Task HandleAsync(SendRecommendationDigest job, CancellationToken ct) =>
        digest.SendAsync(db, email, job.DeliveryId, app.Value.BaseUrl, app.Value.Name, ct);
}
