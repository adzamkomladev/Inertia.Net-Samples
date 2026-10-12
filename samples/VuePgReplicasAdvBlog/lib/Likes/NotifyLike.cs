using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Features.Likes;

public sealed record NotifyLike(Guid PostId, Guid LikerId) : IJob;
