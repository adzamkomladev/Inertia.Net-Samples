using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Features.Uploads;

public sealed record DeleteAssets(string[] Keys) : IJob;
