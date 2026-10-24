using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace CarParker.Infrastructure.Setup;

/// <summary>
/// Shared state for a deployment with more than one CarParker process: Redis for Data Protection, the
/// SignalR backplane and the Inertia cache. When <c>Replicas:Enabled</c> is true, <c>REDIS_URL</c> is
/// required. A one-shot <c>--initialize</c> process may set it false, but still uses the same Redis key
/// ring when a Redis URL is configured.
/// </summary>
public static class ReplicaSetup
{
    public const string SampleName = "CarParkerReplicas";
    const string RedisUrlMissingMessage =
        "REDIS_URL is required when Replicas:Enabled=true (Data Protection keys, SignalR backplane and shared rate limits).";

    public static bool Enabled(IConfiguration configuration) =>
        configuration.GetValue("Replicas:Enabled", true);

    /// <summary>The Redis namespace shared by every process. Never share it with another sample.</summary>
    public static string Namespace(WebApplicationBuilder builder) =>
        Namespace(builder.Configuration, builder.Environment.EnvironmentName);

    public static string Namespace(IConfiguration configuration, string environmentName) =>
        configuration["Replicas:Namespace"] is { } configured && !string.IsNullOrWhiteSpace(configured)
            ? configured.Trim()
            : $"{SampleName}:{environmentName.ToLowerInvariant()}";

    /// <summary>Returns the configured Redis URL, or null when none is configured.</summary>
    public static RedisUrlSettings? Redis(IConfiguration configuration) =>
        RedisUrlSettings.FromConfiguration(configuration);

    public static bool HasRedis(IConfiguration configuration) => Redis(configuration) is not null;

    /// <summary>
    /// Validates the replica settings. With replicas enabled, an absent Redis URL is a startup error; with
    /// replicas disabled (for example the <c>--initialize</c> one-shot), it is allowed.
    /// </summary>
    public static RedisUrlSettings? Validate(IConfiguration configuration)
    {
        var redis = Redis(configuration);
        if (Enabled(configuration) && redis is null) throw new InvalidOperationException(RedisUrlMissingMessage);
        return redis;
    }

    /// <summary>
    /// Registers the Redis connection, Data Protection key ring and Redis health check. Safe to call from
    /// both <see cref="DataSetup"/> and feature setup; only the first call does the work.
    /// </summary>
    public static void AddSharedRedis(WebApplicationBuilder builder)
    {
        var redis = Redis(builder.Configuration)
            ?? throw new InvalidOperationException("REDIS_URL is required to use Redis-backed shared state.");
        if (builder.Services.Any(service => service.ServiceType == typeof(IConnectionMultiplexer))) return;

        var applicationName = Namespace(builder);
        var options = redis.ToConfigurationOptions();
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));
        builder.Services.AddDataProtection().SetApplicationName(applicationName);
        builder.Services.AddOptions<KeyManagementOptions>()
            .Configure<IConnectionMultiplexer>((keyOptions, connection) =>
                keyOptions.XmlRepository = new RedisXmlRepository(
                    () => connection.GetDatabase(), applicationName + ":keys"));
        builder.Services.AddHealthChecks()
            .AddCheck<ReplicaRedisHealthCheck>("redis", failureStatus: HealthStatus.Unhealthy, tags: ["ready"]);
    }

    /// <summary>Adds SignalR and, when configured, the Redis backplane and shared Redis services.</summary>
    public static WebApplicationBuilder AddReplicaServices(this WebApplicationBuilder builder)
    {
        var signalR = builder.Services.AddSignalR();
        var redis = Validate(builder.Configuration);
        if (redis is null) return builder;

        AddSharedRedis(builder);
        if (!Enabled(builder.Configuration)) return builder;

        signalR.AddStackExchangeRedis(options =>
        {
            options.Configuration = redis.ToConfigurationOptions();
            options.Configuration.ChannelPrefix = RedisChannel.Literal(Namespace(builder) + ":signalr");
        });
        return builder;
    }
}

/// <summary>Parsed <c>redis://</c> or <c>rediss://</c> URL. Its string form never includes credentials.</summary>
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
            throw new FormatException("Redis URL must be an absolute redis:// or rediss:// URL without a fragment.");

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

        var port = uri.Port > 0 ? uri.Port : 6379;
        return new RedisUrlSettings(uri.Host, port, database, useTls,
            string.IsNullOrEmpty(username) ? null : username, password);
    }

    public static RedisUrlSettings? FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration["Redis:Url"];
        if (string.IsNullOrWhiteSpace(value)) value = configuration["REDIS_URL"];
        return string.IsNullOrWhiteSpace(value) ? null : Parse(value);
    }

    public ConfigurationOptions ToConfigurationOptions()
    {
        var options = new ConfigurationOptions
        {
            User = Username,
            Password = Password,
            DefaultDatabase = Database,
            Ssl = UseTls,
            AbortOnConnectFail = false,
            ConnectTimeout = 5000,
            SyncTimeout = 2000,
            AsyncTimeout = 2000,
            ConnectRetry = 3,
            KeepAlive = 30,
        };
        options.EndPoints.Add(Host, Port);
        return options;
    }

    public override string ToString() => $"redis{(UseTls ? "s" : "")}://{Host}:{Port}/{Database} (credentials redacted)";
}

public sealed class ReplicaRedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync().WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Shared Redis is unavailable.", exception);
        }
    }
}
