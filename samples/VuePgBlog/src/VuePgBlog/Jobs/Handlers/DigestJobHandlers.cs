using VuePgBlog.Data;
namespace VuePgBlog.Infrastructure;
public sealed class SendRecommendationDigestHandler(AppDb db, DigestService digests, Mailer mailer, IConfiguration config) : IJobHandler<SendRecommendationDigest>
{
    public Task HandleAsync(SendRecommendationDigest m, CancellationToken ct) => digests.SendAsync(db, mailer, m.DeliveryId, m.To, m.Name, config.AppBaseUrl() ?? "http://localhost:5199", ct);
}
