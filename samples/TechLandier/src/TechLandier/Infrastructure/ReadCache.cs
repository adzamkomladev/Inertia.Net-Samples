using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace TechLandier.Infrastructure;

/// <summary>
/// Optional Redis-backed cache for reconstructible read data. Cache failures are ignored; callers
/// always provide an authoritative database factory and must revalidate visibility in the database.
/// </summary>
public sealed class OptionalReadCache(IDistributedCache? cache, OptionalServiceNamespace serviceNamespace)
{
    static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Key(string key) => serviceNamespace.RedisKey(key);

    /// <summary>Reads reconstructible cached data. A null result means absent, disabled, or unavailable.</summary>
    public async Task<T?> TryGetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (cache is null) return null;
        try
        {
            var json = await cache.GetStringAsync(Key(key), cancellationToken);
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }

    /// <summary>Stores reconstructible data without making Redis a source of truth.</summary>
    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default,
        TimeSpan? lifetime = null) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        if (cache is null) return;
        try
        {
            await cache.SetStringAsync(Key(key), JsonSerializer.Serialize(value, JsonOptions), new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = lifetime ?? DefaultLifetime,
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { }
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<Task<T>> databaseFactory,
        CancellationToken cancellationToken = default,
        TimeSpan? lifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(databaseFactory);
        var cacheKey = Key(key);
        if (cache is not null)
        {
            try
            {
                var json = await cache.GetStringAsync(cacheKey, cancellationToken);
                if (!string.IsNullOrEmpty(json))
                {
                    var cached = JsonSerializer.Deserialize<T>(json, JsonOptions);
                    if (cached is not null) return cached;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { /* Optional cache outage or bad entry: read from the database. */ }
        }

        var result = await databaseFactory();
        if (cache is not null)
        {
            try
            {
                var json = JsonSerializer.Serialize(result, JsonOptions);
                await cache.SetStringAsync(cacheKey, json, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = lifetime ?? DefaultLifetime,
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { /* Redis is an optimization; the successful database result still wins. */ }
        }
        return result;
    }
}
