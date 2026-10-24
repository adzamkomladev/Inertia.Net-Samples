using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using TechLandier.Infrastructure;
using Inertia.Net;

namespace TechLandier.Caching;

public sealed class InertiaCacheStoreOptions
{
    public required string Prefix { get; init; }
    public long MaxEntryBytes { get; init; } = 1024 * 1024;

    public static InertiaCacheStoreOptions FromConfiguration(IConfiguration config, IHostEnvironment environment)
    {
        var namespaceName = config["Replicas:Namespace"] ?? $"TechLandierDistributed:{environment.EnvironmentName.ToLowerInvariant()}";
        var version = config["Cache:Version"] is { Length: > 0 } v ? v.Trim() : "v1";
        if (version.Contains(':')) throw new InvalidOperationException("Cache:Version must not contain ':'.");
        var max = config.GetValue("Cache:MaxEntryBytes", 1024 * 1024);
        if (max <= 0) throw new InvalidOperationException("Cache:MaxEntryBytes must be positive.");
        return new InertiaCacheStoreOptions { Prefix = $"{namespaceName}:cache:{version}:", MaxEntryBytes = max };
    }
}

/// <summary>A cache-Redis connection kept separate from the critical Redis connection.</summary>
public sealed class CacheRedisConnection(IConnectionMultiplexer multiplexer)
{
    public IConnectionMultiplexer Multiplexer { get; } = multiplexer;
}

/// <summary>
/// Redis-backed <see cref="IInertiaCacheStore"/>. Failures are misses/no-ops: the page still renders
/// from the database or SSR. Tag sets are rebuilt on demand and expire with their entries.
/// </summary>
public sealed class RedisInertiaCacheStore : IInertiaCacheStore
{
    static readonly TimeSpan FailureLogInterval = TimeSpan.FromSeconds(30);
    static readonly TimeSpan TagSetSlack = TimeSpan.FromMinutes(1);

    // PEXPIRE only when the key has no TTL or the new TTL is larger. The GT overload of KeyExpire
    // treats a missing TTL as infinite, which would leak every new tag set forever.
    const string ExtendTagTtl = """
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < 0 or ttl < tonumber(ARGV[1]) then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        return ttl
        """;

    readonly IDatabase db;
    readonly InertiaCacheStoreOptions options;
    readonly ILogger<RedisInertiaCacheStore> log;
    long nextFailureLogAt;
    int suppressedFailures;

    public RedisInertiaCacheStore(CacheRedisConnection redis, InertiaCacheStoreOptions options, ILogger<RedisInertiaCacheStore> log)
    {
        db = redis.Multiplexer.GetDatabase();
        this.options = options;
        this.log = log;
    }

    string Entry(InertiaCacheKey key) => options.Prefix + key;
    string Tags(string entryKey) => entryKey + ":tags";
    string TagKey(string tag) => options.Prefix + "tag:" + tag;

    public async ValueTask<byte[]?> GetAsync(InertiaCacheKey key, CancellationToken ct)
    {
        try
        {
            var value = await db.StringGetAsync(Entry(key));
            return value.IsNullOrEmpty ? null : (byte[]?)value;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Report(ex);
            return null;
        }
    }

    public async ValueTask SetAsync(InertiaCacheKey key, byte[] value, InertiaCacheEntryOptions entry, CancellationToken ct)
    {
        var entryKey = Entry(key);
        if (entry.Expiration <= TimeSpan.Zero)
        {
            await RemoveAsync(key, ct);
            return;
        }
        if (value.LongLength > options.MaxEntryBytes)
        {
            log.LogDebug("Skipping Inertia cache entry {Key}: {Bytes} bytes exceeds Cache:MaxEntryBytes", entryKey, value.LongLength);
            await RemoveAsync(key, ct);
            return;
        }

        try
        {
            var tagsKey = Tags(entryKey);
            var oldTags = await ReadTagsAsync(tagsKey);
            foreach (var tag in oldTags) await db.SetRemoveAsync(TagKey(tag), entryKey);

            await db.StringSetAsync(entryKey, value, entry.Expiration);
            await db.StringSetAsync(tagsKey, JsonSerializer.Serialize(entry.Tags), entry.Expiration);

            var ttlMilliseconds = (long)(entry.Expiration + TagSetSlack).TotalMilliseconds;
            foreach (var tag in entry.Tags)
            {
                var tagKey = TagKey(tag);
                await db.SetAddAsync(tagKey, entryKey);
                await db.ScriptEvaluateAsync(ExtendTagTtl, [tagKey], [ttlMilliseconds]);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Report(ex);
        }
    }

    public async ValueTask RemoveAsync(InertiaCacheKey key, CancellationToken ct)
    {
        var entryKey = Entry(key);
        try
        {
            var tagsKey = Tags(entryKey);
            var tags = await ReadTagsAsync(tagsKey);
            foreach (var tag in tags) await db.SetRemoveAsync(TagKey(tag), entryKey);
            await DeleteAsync(entryKey);
            await DeleteAsync(tagsKey);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Report(ex);
        }
    }

    public async ValueTask RemoveByTagAsync(string tag, CancellationToken ct)
    {
        var tagKey = TagKey(tag);
        try
        {
            // SSCAN in pages: a global tag (inertia:props / inertia:ssr) can be large, so do not
            // materialize the whole set before deleting entries.
            var batch = new List<RedisValue>(256);
            await foreach (var member in db.SetScanAsync(tagKey, pageSize: 256))
            {
                if (ct.IsCancellationRequested) break;
                batch.Add(member);
                if (batch.Count < 256) continue;
                await RemoveMembersAsync(tag, batch);
                batch.Clear();
            }
            if (batch.Count > 0) await RemoveMembersAsync(tag, batch);
            await DeleteAsync(tagKey);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Report(ex);
        }
    }

    async Task RemoveMembersAsync(string tag, List<RedisValue> members)
    {
        foreach (var member in members)
        {
            var entryKey = (string)member!;
            var tagsKey = Tags(entryKey);
            var tags = await ReadTagsAsync(tagsKey);
            // The member may have been overwritten with different tags since the scan; do not
            // delete a newer value that no longer carries this tag.
            if (tags.Count == 0 || !tags.Contains(tag, StringComparer.Ordinal)) continue;
            foreach (var other in tags.Where(t => !string.Equals(t, tag, StringComparison.Ordinal)))
                await db.SetRemoveAsync(TagKey(other), entryKey);
            await DeleteAsync(entryKey);
            await DeleteAsync(tagsKey);
        }
    }

    async Task<List<string>> ReadTagsAsync(string tagsKey)
    {
        var value = await db.StringGetAsync(tagsKey);
        if (value.IsNullOrEmpty) return [];
        try { return JsonSerializer.Deserialize<string[]>((string)value!)?.ToList() ?? []; }
        catch (JsonException) { return []; }
    }

    async Task DeleteAsync(string key) => await db.ExecuteAsync("UNLINK", key);

    void Report(Exception ex)
    {
        var now = Environment.TickCount64;
        var next = Interlocked.Read(ref nextFailureLogAt);
        if (now < next)
        {
            Interlocked.Increment(ref suppressedFailures);
            return;
        }
        var suppressed = Interlocked.Exchange(ref suppressedFailures, 0);
        Interlocked.Exchange(ref nextFailureLogAt, now + (long)FailureLogInterval.TotalMilliseconds);
        log.LogWarning(ex, "Inertia cache Redis operation failed; continuing as a miss ({Suppressed} similar failures suppressed)", suppressed);
    }
}

public sealed class CacheRedisHealthCheck(CacheRedisConnection redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.Multiplexer.GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            // A cache outage degrades performance, not correctness: keep the replica in rotation.
            return HealthCheckResult.Degraded("Cache Redis is unavailable.", ex);
        }
    }
}

/// <summary>MainApp adapter: delegates to Inertia's cache facade, which is backed by the shared store.</summary>
public sealed class InertiaPropCacheInvalidator(IInertiaCache cache) : IPropCacheInvalidator
{
    public async ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default) =>
        await cache.RemoveByTagAsync(tag, ct);
}

/// <summary>Worker adapter: invalidates the shared store directly; no Inertia renderer is needed here.</summary>
public sealed class RedisPropCacheInvalidator(IInertiaCacheStore store) : IPropCacheInvalidator
{
    public async ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default) =>
        await store.RemoveByTagAsync(tag, ct);
}

public sealed class NoOpPropCacheInvalidator : IPropCacheInvalidator
{
    public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default) => ValueTask.CompletedTask;
}
