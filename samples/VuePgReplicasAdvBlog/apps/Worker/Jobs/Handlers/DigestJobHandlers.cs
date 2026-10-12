using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Digests;
using VuePgReplicasAdvBlog.Features.Email;
namespace VuePgReplicasAdvBlog.Infrastructure;

public sealed class SendRecommendationDigestHandler(AppDb db, DigestService digests, Mailer mailer, IConfiguration config) : IJobHandler<SendRecommendationDigest>
{
    public Task HandleAsync(SendRecommendationDigest m, CancellationToken ct) => digests.SendAsync(db, mailer, m.DeliveryId, m.To, m.Name, config.AppBaseUrl() ?? "http://localhost:5199", ct);
}
