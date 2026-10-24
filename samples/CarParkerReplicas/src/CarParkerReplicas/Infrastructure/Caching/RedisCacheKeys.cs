using Inertia.Net;

namespace CarParker.Infrastructure.Caching;

/// <summary>
/// Redis keys used by <see cref="RedisInertiaCacheStore"/>. Kept in one place so key layout, cache
/// versioning and tests cannot drift. All keys include the sample/environment namespace and the cache
/// format version, and all entry/tag keys use a colon-separated flat form.
/// </summary>
public static class RedisCacheKeys
{
    /// <summary><c>{namespace}:cache:{version}:</c>. The namespace and version must match in every process.</summary>
    public static string Prefix(string sampleNamespace, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sampleNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (sampleNamespace.Any(char.IsControl) || version.Any(char.IsControl))
            throw new ArgumentException("Cache namespace and version must not contain control characters.");
        if (version.IndexOf(':') >= 0 || version.Any(char.IsWhiteSpace))
            throw new ArgumentException("Cache version must not contain colons or whitespace.", nameof(version));
        return $"{sampleNamespace}:cache:{version}:";
    }

    /// <summary>The full key of an Inertia cache entry.</summary>
    public static string Entry(string prefix, InertiaCacheKey key) => prefix + key.ToString();

    /// <summary>Reverse index: the tags an entry currently carries.</summary>
    public static string Tags(string prefix, string entryKey) => prefix + "tags:" + entryKey;

    /// <summary>The set of entries carrying a tag.</summary>
    public static string Tag(string prefix, string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return prefix + "tag:" + tag;
    }
}
