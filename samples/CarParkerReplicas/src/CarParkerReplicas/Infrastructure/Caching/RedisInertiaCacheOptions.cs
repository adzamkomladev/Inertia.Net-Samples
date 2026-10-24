using System.Globalization;

namespace CarParker.Infrastructure.Caching;

/// <summary>
/// Settings for the shared Inertia cache. <see cref="Prefix"/> already contains the sample namespace
/// and <c>Cache:Version</c>; the raw version is exposed separately for diagnostics.
/// </summary>
public sealed record RedisInertiaCacheOptions(
    string Prefix,
    string Version,
    long MaxEntryBytes,
    TimeSpan TagTtlPadding,
    TimeSpan FailureLogInterval)
{
    public static RedisInertiaCacheOptions FromConfiguration(IConfiguration configuration, string sampleNamespace)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var version = VersionFrom(configuration);
        var maxEntryBytes = configuration.GetValue<long?>("Cache:MaxEntryBytes") ?? 0;
        if (maxEntryBytes < 0)
            throw new InvalidOperationException("Cache:MaxEntryBytes must be zero or a positive number of bytes.");
        var paddingSeconds = configuration.GetValue<int?>("Cache:TagTtlPaddingSeconds") ?? 60;
        if (paddingSeconds < 0)
            throw new InvalidOperationException("Cache:TagTtlPaddingSeconds must be zero or positive.");
        var failureLogSeconds = configuration.GetValue<int?>("Cache:FailureLogSeconds") ?? 30;
        if (failureLogSeconds < 1)
            throw new InvalidOperationException("Cache:FailureLogSeconds must be at least one second.");

        return new RedisInertiaCacheOptions(
            RedisCacheKeys.Prefix(sampleNamespace, version),
            version,
            maxEntryBytes,
            TimeSpan.FromSeconds(paddingSeconds),
            TimeSpan.FromSeconds(failureLogSeconds));
    }

    /// <summary><c>Cache:Version</c> first, then <c>CACHE_VERSION</c>; <c>v1</c> when neither is set.</summary>
    public static string VersionFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration["Cache:Version"];
        if (string.IsNullOrWhiteSpace(value)) value = configuration["CACHE_VERSION"];
        if (string.IsNullOrWhiteSpace(value)) return "v1";
        var version = value.Trim();
        if (version.IndexOf(':') >= 0 || version.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("Cache:Version must not contain colons or whitespace.");
        return version;
    }

    public string Describe() =>
        string.Create(CultureInfo.InvariantCulture,
            $"prefix={Prefix}, maxEntryBytes={MaxEntryBytes}, tagTtlPadding={TagTtlPadding.TotalSeconds}s");
}
