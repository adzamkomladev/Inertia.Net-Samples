using StackExchange.Redis;

namespace TechLandier.Infrastructure;

/// <summary>The critical Redis connection (Data Protection, backplane, limits, alert gates).</summary>
public sealed class CriticalRedisConnection(IConnectionMultiplexer multiplexer)
{
    public IConnectionMultiplexer Multiplexer { get; } = multiplexer;
}

public static class ReplicaSetup
{
    public static string Namespace(IHostApplicationBuilder builder) =>
        builder.Configuration["Replicas:Namespace"] ?? $"TechLandierDistributed:{builder.Environment.EnvironmentName.ToLowerInvariant()}";

    public static bool Enabled(IConfiguration config) => config.GetValue("Replicas:Enabled", true);
}

public sealed record RedisUrlSettings(ConfigurationOptions Options)
{
    public static RedisUrlSettings? FromConfiguration(IConfiguration config, string key = "Redis:Url")
    {
        var value = config[key];
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("redis" or "rediss")
            || string.IsNullOrEmpty(uri.Host))
            throw new InvalidOperationException($"{key} must be a redis:// or rediss:// URL with a host.");

        var database = 0;
        if (uri.AbsolutePath is { Length: > 1 } path
            && (!int.TryParse(path.Trim('/'), out database) || database < 0))
            throw new InvalidOperationException($"{key} has an invalid Redis database in its path.");

        var options = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectTimeout = 5000,
            SyncTimeout = 2000,
            AsyncTimeout = 2000,
            DefaultDatabase = database,
            Ssl = uri.Scheme == "rediss",
        };
        if (uri.UserInfo.Length > 0)
        {
            var credentials = uri.UserInfo.Split(':', 2);
            options.User = Uri.UnescapeDataString(credentials[0]);
            if (credentials.Length == 2) options.Password = Uri.UnescapeDataString(credentials[1]);
        }
        var port = uri.Port < 0 ? 6379 : uri.Port;
        if (port is < 1 or > 65535) throw new InvalidOperationException($"{key} has an invalid Redis port.");
        options.EndPoints.Add(uri.Host, port);
        return new RedisUrlSettings(options);
    }
}
