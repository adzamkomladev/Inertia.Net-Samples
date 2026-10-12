using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Discovery;
namespace VuePgReplicasAdvBlog.Infrastructure;

public sealed class RefreshInterestProfileHandler(AppDb db, DiscoveryService discovery) : IJobHandler<RefreshInterestProfile>
{
    public Task HandleAsync(RefreshInterestProfile m, CancellationToken ct) => discovery.RefreshProfileAsync(db, m.UserId, ct);
}
