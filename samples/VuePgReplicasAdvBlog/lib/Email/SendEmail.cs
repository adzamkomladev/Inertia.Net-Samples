using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Features.Email;

public sealed record SendEmail(string To, string Subject, string Html, string Text) : IJob;
