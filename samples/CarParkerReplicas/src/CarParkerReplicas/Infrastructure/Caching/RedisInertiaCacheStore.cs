using System.Diagnostics;
using System.Text.Json;
using Inertia.Net;
using StackExchange.Redis;

namespace CarParker.Infrastructure.Caching;

/// <summary>
/// An <see cref="IInertiaCacheStore"/> shared by every app replica through Redis.
///
/// <para>Keys are <c>{namespace}:cache:{version}:inertia:prop:{key}</c> and
/// <c>{namespace}:cache:{version}:inertia:ssr:{hash}</c>. Each entry has a reverse index at
/// <c>{prefix}tags:{entryKey}</c> holding its tags, and each tag has a set at
/// <c>{prefix}tag:{tag}</c> holding entry keys.</para>
///
/// <para>The cache is not a source of truth. Every Redis failure is logged at a throttled rate and
/// treated as a miss/no-op so a cache outage only costs database reads and SSR renders.</para>
/// </summary>
public sealed class RedisInertiaCacheStore : IInertiaCacheStore
{
    /// <summary>
    /// Extends a tag set's expiry only when the new TTL is longer. <c>KeyExpire(..., GreaterThanCurrentExpiry)</c>
    /// is not a substitute: a key with no TTL is treated as infinite, so GT is a no-op on a brand-new tag set.
    /// This script therefore handles both "no expiry" (-1) and "missing" (-2) by setting the TTL.
    /// </summary>
    public const string ExtendTagTtlScript = """
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < 0 or ttl < tonumber(ARGV[1]) then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        return ttl
        """;

    // UNLINK frees large values on a background thread. It is a single-key command, so it is safe on a
    // cluster even though the surrounding invalidation performs separate commands for different keys.
    public const string UnlinkScript = "return redis.call('UNLINK', KEYS[1])";

    readonly IDatabase database;
    readonly RedisInertiaCacheOptions options;
    readonly ILogger<RedisInertiaCacheStore> logger;
    long lastWarningTimestamp;

    public RedisInertiaCacheStore(
        IConnectionMultiplexer redis,
        RedisInertiaCacheOptions options,
        ILogger<RedisInertiaCacheStore> logger)
    {
        ArgumentNullException.ThrowIfNull(redis);
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        database = redis.GetDatabase();
    }

    public async ValueTask<byte[]?> GetAsync(InertiaCacheKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var value = await database.StringGetAsync(EntryKey(key));
            return value.IsNull ? null : (byte[])value!;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            ReportFailure("get", exception);
            return null;
        }
    }

    public async ValueTask SetAsync(
        InertiaCacheKey key,
        byte[] value,
        InertiaCacheEntryOptions entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        if (entry.Expiration <= TimeSpan.Zero)
        {
            await RemoveAsync(key, cancellationToken);
            return;
        }

        if (options.MaxEntryBytes > 0 && value.LongLength > options.MaxEntryBytes)
        {
            // Drop any previous value too: keeping a smaller, older entry while refusing the new one would
            // make the oversize guard serve stale data for the rest of the old TTL.
            await RemoveAsync(key, cancellationToken);
            if (ShouldLog()) logger.LogWarning(
                "Inertia cache skipped a {Bytes} byte entry because Cache:MaxEntryBytes is {MaxBytes} ({Prefix}).",
                value.LongLength, options.MaxEntryBytes, options.Prefix);
            return;
        }

        var entryKey = EntryKey(key);
        var tagsKey = RedisCacheKeys.Tags(options.Prefix, entryKey);
        var tags = NormalizeTags(entry.Tags);
        try
        {
            // Drop the old reverse-index memberships first. This also handles an overwrite with the same tag.
            foreach (var oldTag in await ReadTagsAsync(tagsKey))
                await database.SetRemoveAsync(TagKey(oldTag), entryKey);

            await database.StringSetAsync(entryKey, value, entry.Expiration);
            await database.StringSetAsync(tagsKey, JsonSerializer.Serialize(tags), entry.Expiration);

            var tagTtl = entry.Expiration + options.TagTtlPadding;
            var tagTtlMilliseconds = Math.Max(1, (long)tagTtl.TotalMilliseconds);
            foreach (var tag in tags)
            {
                var tagKey = TagKey(tag);
                await database.SetAddAsync(tagKey, entryKey);
                await database.ScriptEvaluateAsync(ExtendTagTtlScript, [tagKey], [tagTtlMilliseconds]);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            ReportFailure("set", exception);
        }
    }

    public async ValueTask RemoveAsync(InertiaCacheKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entryKey = EntryKey(key);
        var tagsKey = RedisCacheKeys.Tags(options.Prefix, entryKey);
        try
        {
            foreach (var tag in await ReadTagsAsync(tagsKey))
                await database.SetRemoveAsync(TagKey(tag), entryKey);
            await DeleteAsync(entryKey);
            await DeleteAsync(tagsKey);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            ReportFailure("remove", exception);
        }
    }

    /// <summary>
    /// Removes every entry carrying <paramref name="tag"/>. SSCAN avoids loading a large global tag set
    /// at once, and the reverse index is checked before deleting so an entry overwritten with different
    /// tags is preserved. Keys are deleted individually because Redis Cluster routes each key by hash slot.
    /// </summary>
    public async ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        cancellationToken.ThrowIfCancellationRequested();
        var tagKey = TagKey(tag);
        try
        {
            var batch = new List<RedisValue>(256);
            await foreach (var member in database.SetScanAsync(tagKey, pageSize: 256))
            {
                cancellationToken.ThrowIfCancellationRequested();
                batch.Add(member);
                if (batch.Count < 256) continue;
                await RemoveMembersAsync(tag, batch);
                batch.Clear();
            }
            if (batch.Count > 0) await RemoveMembersAsync(tag, batch);

            // The set is deleted after the scan. A concurrent SetAsync may add a member while invalidation
            // runs; that entry keeps its value but loses its tag membership until it expires (documented).
            await DeleteAsync(tagKey);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            ReportFailure("remove-by-tag", exception);
        }
    }

    async Task RemoveMembersAsync(string tag, List<RedisValue> members)
    {
        foreach (var member in members)
        {
            var entryKey = member.ToString();
            if (!entryKey.StartsWith(options.Prefix, StringComparison.Ordinal)) continue;

            var tags = await ReadTagsAsync(RedisCacheKeys.Tags(options.Prefix, entryKey));
            if (!tags.Contains(tag, StringComparer.Ordinal)) continue; // stale membership from an overwrite

            foreach (var otherTag in tags)
            {
                if (!string.Equals(otherTag, tag, StringComparison.Ordinal))
                    await database.SetRemoveAsync(TagKey(otherTag), entryKey);
            }
            await DeleteAsync(entryKey);
            await DeleteAsync(RedisCacheKeys.Tags(options.Prefix, entryKey));
        }
    }

    async Task<string[]> ReadTagsAsync(string tagsKey)
    {
        var raw = await database.StringGetAsync(tagsKey);
        if (raw.IsNull) return [];
        try
        {
            return JsonSerializer.Deserialize<string[]>(raw.ToString()) ?? [];
        }
        catch (JsonException exception)
        {
            if (ShouldLog()) logger.LogWarning(exception,
                "Inertia cache reverse tag index {TagsKey} is unreadable; treating the entry as untagged.",
                tagsKey);
            return [];
        }
    }

    async Task DeleteAsync(string key) => await database.ScriptEvaluateAsync(UnlinkScript, [key]);

    string EntryKey(InertiaCacheKey key) => RedisCacheKeys.Entry(options.Prefix, key);

    string TagKey(string tag) => RedisCacheKeys.Tag(options.Prefix, tag);

    static string[] NormalizeTags(IReadOnlyList<string> tags) =>
        tags.Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    static bool IsRedisFailure(Exception exception) =>
        exception is RedisException or TimeoutException or IOException or ObjectDisposedException;

    bool ShouldLog()
    {
        var now = Stopwatch.GetTimestamp();
        var last = Interlocked.Read(ref lastWarningTimestamp);
        if (last != 0 && Stopwatch.GetElapsedTime(last, now) < options.FailureLogInterval) return false;
        return Interlocked.CompareExchange(ref lastWarningTimestamp, now, last) == last;
    }

    void ReportFailure(string operation, Exception exception)
    {
        if (ShouldLog()) logger.LogWarning(exception,
            "Inertia cache {Operation} failed; treating it as a miss/no-op for {Prefix}. " +
            "The cache is not a source of truth.", operation, options.Prefix);
    }
}
