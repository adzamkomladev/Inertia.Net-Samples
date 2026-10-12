namespace VuePgReplicasAdvBlog.Infrastructure;

public static class AppConfiguration
{
    public static string? AppBaseUrl(this IConfiguration config) =>
        config["App:BaseUrl"] is { } url && !string.IsNullOrWhiteSpace(url) ? url.Trim().TrimEnd('/') : null;
}
