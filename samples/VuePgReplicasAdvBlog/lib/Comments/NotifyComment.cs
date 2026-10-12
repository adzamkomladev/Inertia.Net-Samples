using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Features.Comments;

public sealed record NotifyComment(Guid CommentId) : IJob;
