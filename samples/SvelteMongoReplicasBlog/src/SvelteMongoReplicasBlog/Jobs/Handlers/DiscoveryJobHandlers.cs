using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Features.Discovery;
namespace SvelteMongoReplicasBlog.Infrastructure;
public sealed class RefreshInterestProfileHandler(AppDb db, DiscoveryService discovery) : IJobHandler<RefreshInterestProfile>
{
    public Task HandleAsync(RefreshInterestProfile m, CancellationToken ct) => discovery.RefreshProfileAsync(db, m.UserId, ct);
}
