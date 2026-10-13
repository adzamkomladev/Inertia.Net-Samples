namespace VuePgReplicasAdvBlog.Infrastructure;

public static class AppConfiguration
{
    public static string? AppBaseUrl(this IConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config["App:BaseUrl"])) return null;
        var value = config["App:BaseUrl"]!.Trim().TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https")
            || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0)
            throw new InvalidOperationException("APP_BASE_URL must be an absolute HTTP(S) gateway URL without credentials, query or fragment.");
        return value;
    }

    public static string RequiredAppBaseUrl(this IConfiguration config) => config.AppBaseUrl()
        ?? throw new InvalidOperationException("Set APP_BASE_URL to the public gateway URL.");
}
