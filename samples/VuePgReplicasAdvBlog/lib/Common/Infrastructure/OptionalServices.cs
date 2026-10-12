using System.Globalization;

namespace VuePgReplicasAdvBlog.Infrastructure;

/// <summary>Stable per-sample/per-environment names for shared optional backends.</summary>
public sealed class OptionalServiceNamespace
{
    public OptionalServiceNamespace(string sampleFolderName, string environmentName, string? meilisearchIndex = null)
    {
        SampleFolderName = RequiredPart(sampleFolderName, nameof(sampleFolderName));
        EnvironmentName = RequiredPart(environmentName, nameof(environmentName));
        if (!string.IsNullOrWhiteSpace(meilisearchIndex))
        {
            if (meilisearchIndex.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
                throw new ArgumentException("MEILISEARCH_INDEX must contain only ASCII letters, numbers, hyphens or underscores.", nameof(meilisearchIndex));
            PostsIndex = meilisearchIndex;
        }
    }

    private string? PostsIndex { get; }

    public string SampleFolderName { get; }
    public string EnvironmentName { get; }

    public string RedisPrefix => $"{SampleFolderName}:{EnvironmentName}:";
    public string RedisKey(string key) => RedisPrefix + RequiredPart(key, nameof(key));
    public string MeilisearchIndex(string indexName) =>
        indexName == "posts" && PostsIndex is not null ? PostsIndex
            : $"{SampleFolderName}-{EnvironmentName}-{RequiredPart(indexName, nameof(indexName))}";

    static string RequiredPart(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

/// <summary>Parsed Redis URL. Its string representation deliberately never includes credentials.</summary>
public sealed class RedisUrlSettings(
    string host,
    int port,
    int database,
    bool useTls,
    string? username,
    string? password)
{
    public string Host { get; } = host;
    public int Port { get; } = port;
    public int Database { get; } = database;
    public bool UseTls { get; } = useTls;
    public string? Username { get; } = username;
    public string? Password { get; } = password;

    public static RedisUrlSettings Parse(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != "redis" && uri.Scheme != "rediss")
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new FormatException("Redis URL must be an absolute redis:// or rediss:// URL.");

        var databaseText = uri.AbsolutePath.Trim('/');
        var database = 0;
        if (databaseText.Length > 0
            && (!int.TryParse(databaseText, NumberStyles.None, CultureInfo.InvariantCulture, out database) || database < 0))
            throw new FormatException("Redis URL database path must be a non-negative integer.");

        var username = (string?)null;
        var password = (string?)null;
        var userInfo = uri.GetComponents(UriComponents.UserInfo, UriFormat.UriEscaped);
        if (userInfo.Length > 0)
        {
            var separator = userInfo.IndexOf(':');
            if (separator < 0)
                password = Uri.UnescapeDataString(userInfo);
            else
            {
                username = Uri.UnescapeDataString(userInfo[..separator]);
                password = Uri.UnescapeDataString(userInfo[(separator + 1)..]);
            }
        }

        var useTls = uri.Scheme == "rediss";
        var query = uri.Query.TrimStart('?');
        if (query.Length > 0)
        {
            foreach (var parameter in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = parameter.Split('=', 2);
                var key = Uri.UnescapeDataString(pieces[0]);
                var setting = pieces.Length == 2 ? Uri.UnescapeDataString(pieces[1]) : "";
                if (!string.Equals(key, "ssl", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(key, "tls", StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("Redis URL contains an unsupported query parameter.");
                if (!bool.TryParse(setting, out useTls))
                    throw new FormatException("Redis URL TLS query value must be true or false.");
            }
        }

        var port = uri.IsDefaultPort ? 6379 : uri.Port;
        return new RedisUrlSettings(uri.Host, port, database, useTls, username, password);
    }

    public static RedisUrlSettings? FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.GetConnectionString("redis") is { Length: > 0 } connectionString)
        {
            var options = StackExchange.Redis.ConfigurationOptions.Parse(connectionString);
            var endpoint = options.EndPoints.Single();
            var (host, port) = endpoint switch
            {
                System.Net.DnsEndPoint dns => (dns.Host, dns.Port),
                System.Net.IPEndPoint ip => (ip.Address.ToString(), ip.Port),
                _ => throw new FormatException("Unsupported Redis endpoint.")
            };
            return new RedisUrlSettings(host, port, options.DefaultDatabase ?? 0, options.Ssl, options.User, options.Password);
        }
        var value = configuration["Redis:Url"];
        if (string.IsNullOrWhiteSpace(value)) value = configuration["REDIS_URL"];
        return string.IsNullOrWhiteSpace(value) ? null : Parse(value);
    }

    public override string ToString() => $"redis{(UseTls ? "s" : "")}://{Host}:{Port}/{Database} (credentials redacted)";
}
